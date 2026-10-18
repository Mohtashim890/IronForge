// Reads QR codes from the device camera for the front desk and the kiosk.
// Uses the browser's BarcodeDetector where it exists (Chrome on Android,
// macOS, ChromeOS) and the bundled jsQR library elsewhere (e.g. Windows).

let jsQrLoading = null;

function loadJsQr() {
    if (window.jsQR) {
        return Promise.resolve();
    }

    jsQrLoading ??= new Promise((resolve, reject) => {
        const script = document.createElement("script");
        script.src = new URL("./vendor/jsQR.js", import.meta.url).href;
        script.onload = () => resolve();
        script.onerror = () => {
            jsQrLoading = null;
            reject(new Error("Could not load the QR reader."));
        };
        document.head.appendChild(script);
    });

    return jsQrLoading;
}

async function createDetector() {
    if (!("BarcodeDetector" in window)) {
        return null;
    }

    try {
        const formats = await window.BarcodeDetector.getSupportedFormats();
        return formats.includes("qr_code") ? new window.BarcodeDetector({ formats: ["qr_code"] }) : null;
    } catch {
        return null;
    }
}

export async function start(video, dotnet) {
    stop(video);

    if (!navigator.mediaDevices?.getUserMedia) {
        throw new Error("This browser cannot use the camera here. Use HTTPS, or a USB scanner.");
    }

    const stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: { ideal: "environment" }, width: { ideal: 1280 }, height: { ideal: 720 } },
        audio: false
    });

    const state = { stream, stopped: false, timer: null, last: null, lastAt: 0 };
    video._ifScanner = state;

    video.setAttribute("playsinline", "");
    video.muted = true;
    video.srcObject = stream;
    await video.play();

    const detector = await createDetector();

    if (!detector) {
        await loadJsQr();
    }

    const canvas = document.createElement("canvas");
    const context = canvas.getContext("2d", { willReadFrequently: true });

    const read = async () => {
        if (detector) {
            const codes = await detector.detect(video);
            return codes.length > 0 ? codes[0].rawValue : null;
        }

        // Scale down: decoding a 640px frame is fast and plenty for a phone screen.
        const scale = Math.min(1, 640 / Math.max(video.videoWidth, video.videoHeight));
        canvas.width = Math.round(video.videoWidth * scale);
        canvas.height = Math.round(video.videoHeight * scale);
        context.drawImage(video, 0, 0, canvas.width, canvas.height);

        const image = context.getImageData(0, 0, canvas.width, canvas.height);
        const code = window.jsQR(image.data, canvas.width, canvas.height, { inversionAttempts: "attemptBoth" });

        return code ? code.data : null;
    };

    const tick = async () => {
        if (state.stopped) {
            return;
        }

        if (video.readyState >= 2 && video.videoWidth > 0) {
            try {
                const text = await read();
                const now = Date.now();

                // The same code stays in front of the camera for a while: report it once.
                if (text && (text !== state.last || now - state.lastAt > 5000)) {
                    state.last = text;
                    state.lastAt = now;
                    await dotnet.invokeMethodAsync("OnQrCode", text);
                }
            } catch {
                // A frame that cannot be read; try the next one.
            }
        }

        if (!state.stopped) {
            state.timer = setTimeout(tick, 200);
        }
    };

    tick();
}

export function stop(video) {
    const state = video?._ifScanner;

    if (!state) {
        return;
    }

    state.stopped = true;
    clearTimeout(state.timer);
    state.stream.getTracks().forEach(track => track.stop());
    video.srcObject = null;
    video._ifScanner = null;
}
