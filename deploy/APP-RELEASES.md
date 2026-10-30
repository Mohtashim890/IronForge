# Releasing the IronForge member app (Android and Windows)

The member app is built and published by GitHub Actions
(`.github/workflows/release-apps.yml`). Pushing a tag such as `app-v1.2.0`
builds both apps and publishes them on a GitHub Release:

| File | For | Notes |
|---|---|---|
| `IronForge.apk` | Android 7.0 or later | Signed with the release keystore; installs and updates directly |
| `IronForge-Setup.exe` | Windows 10 (1809) or 11, 64-bit | Installer for the self-contained app; no admin rights needed |
| `*.sha256` | Everyone | SHA-256 checksum of each file |

Permanent links to the newest stable version (share these, they never change):

- Release page: https://github.com/Mohtashim890/IronForge/releases/latest
- Android: https://github.com/Mohtashim890/IronForge/releases/latest/download/IronForge.apk
- Windows: https://github.com/Mohtashim890/IronForge/releases/latest/download/IronForge-Setup.exe

Release builds connect to the production API (`https://api.mohtashim.dev/`, from
`IronForge/Resources/Raw/appsettings.json`).

---

## 1. One-time setup: keystore secrets

The Android app must be signed with **the same keystore for every release**.
An APK signed with any other key cannot update an installed app: users would
have to uninstall it (losing its data) and install again. The workflow uses
your existing release keystore; never create a new one.

Your signing files are in `%USERPROFILE%\IronForgeSigning`:

- `ironforge-release.keystore`: the keystore (alias `ironforge-release`)
- `signing-password.txt`: the password, on the first line (the second line repeats it)

**Back these up now** (password manager or encrypted drive). If they are lost,
existing Android installs cannot be updated.

### 1.1 Encode the keystore

GitHub secrets hold text, so the keystore is stored base64-encoded. In
PowerShell:

```powershell
$ks = "$env:USERPROFILE\IronForgeSigning\ironforge-release.keystore"
[Convert]::ToBase64String([IO.File]::ReadAllBytes($ks)) | Set-Clipboard
```

The encoded keystore is now on the clipboard (one long line).

### 1.2 Add the secrets

1. Open https://github.com/Mohtashim890/IronForge/settings/secrets/actions
   (repository **Settings** > **Secrets and variables** > **Actions**).
2. On the **Secrets** tab, choose **New repository secret** and add:

   | Name | Value |
   |---|---|
   | `ANDROID_KEYSTORE_BASE64` | Paste the clipboard from step 1.1 |
   | `ANDROID_KEYSTORE_PASSWORD` | The first line of `signing-password.txt` (no spaces or line breaks) |

   Optional, only if your keystore differs from the default setup:

   | Name | Value |
   |---|---|
   | `ANDROID_KEY_PASSWORD` | Key password, when it differs from the keystore password |
   | `ANDROID_KEY_ALIAS` | Key alias, when it is not `ironforge-release` |

3. Clear the clipboard afterwards (copy something else).

Alternatively, with the GitHub CLI (it prompts for the password, so it never
appears in your shell history):

```powershell
$ks = "$env:USERPROFILE\IronForgeSigning\ironforge-release.keystore"
[Convert]::ToBase64String([IO.File]::ReadAllBytes($ks)) | gh secret set ANDROID_KEYSTORE_BASE64
gh secret set ANDROID_KEYSTORE_PASSWORD
```

### 1.3 Add the certificate check (recommended)

This makes a release fail when it would be signed with the wrong key.

1. Same page, **Variables** tab > **New repository variable**.
2. Name `ANDROID_CERT_SHA256`, value:

   ```
   95:DF:C4:7B:6F:69:73:46:FD:8D:AF:B8:2C:47:0B:E4:E2:01:AC:52:D7:E4:9F:E1:C4:FC:2B:8B:FC:11:61:A3
   ```

   This is the fingerprint of `ironforge-release.keystore`; it is public
   information, not a secret. To print it yourself:

   ```powershell
   $d = "$env:USERPROFILE\IronForgeSigning"
   keytool -list -v -keystore "$d\ironforge-release.keystore" -storepass:file "$d\signing-password.txt" | Select-String SHA256
   ```

### 1.4 Check the workflow permission

The workflow creates releases, so it asks for `contents: write`. This works on
a personal repository by default. If a run fails with "Resource not accessible
by integration", open **Settings** > **Actions** > **General** >
**Workflow permissions** and choose **Read and write permissions**.

---

## 2. Test the pipeline (no release)

Before the first real release, do a trial build:

1. https://github.com/Mohtashim890/IronForge/actions > **IronForge App Release**
   > **Run workflow**, enter a version such as `0.0.1`, leave **Publish a
   release** unticked, **Run workflow**.
2. When it finishes, open the run: under **Artifacts** are
   `ironforge-android-0.0.1` and `ironforge-windows-0.0.1` (zip files with the
   APK and the installer, kept 14 days). No release is created and nothing is
   public.
3. Install them on a test phone and PC (section 4).

The workflow file must be on the default branch (`main`) for **Run workflow**
to appear.

---

## 3. Publish a release

1. Pick the version. Format `MAJOR.MINOR.PATCH`, e.g. `1.2.0`; MINOR and PATCH
   at most 99. **Each release must be higher than the previous one**: Android
   refuses to install an older version over a newer one.
2. Start the release, in either of two ways:

   - **From GitHub (no command line):** **Actions** > **IronForge App Release**
     > **Run workflow**. Choose the branch to ship (normally `main`), enter the
     version (e.g. `1.2.0`), tick **Publish a release**, **Run workflow**. The
     release creates the tag `app-v1.2.0` on the newest commit of that branch.
     If that tag already exists, the run stops; use a higher version.
   - **From a terminal:** tag the commit you want to ship and push the tag:

     ```bash
     git tag app-v1.2.0
     git push origin app-v1.2.0
     ```

   A run started with **Run workflow** and **Publish a release** unticked is
   only a test build (section 2): its jobs **Attach to release** and
   **Publish release** show as skipped, and nothing appears under Releases.

3. Watch **Actions** > **IronForge App Release** (about 15 to 25 minutes). The
   workflow:
   1. creates a **draft** release `IronForge app 1.2.0`;
   2. builds the signed APK and the Windows installer in parallel and attaches
      them with their checksums;
   3. publishes the release and marks it **Latest**, which moves the permanent
      links in the table above to it.

   If a build fails, the release stays a draft: fix the problem and re-run the
   failed jobs (the draft is reused and its files replaced), or delete the draft
   and the tag and tag again.

**Test versions for a few people:** a tag with a suffix, e.g.
`app-v1.3.0-beta.1`, is published as a **pre-release**. It is not marked
Latest, so the permanent links keep pointing at the last stable version; share
its own release page instead.

### How the version is set

The tag sets the version; nothing in the project file needs editing.

| Tag | Version shown | Android version code |
|---|---|---|
| `app-v1.0.0` | 1.0.0 | 10000 |
| `app-v1.2.3` | 1.2.3 | 10203 |
| `app-v2.0.0` | 2.0.0 | 20000 |
| `app-v1.3.0-beta.1` | 1.3.0-beta.1 (Windows app: 1.3.0) | 10300 |

The version code is `MAJOR * 10000 + MINOR * 100 + PATCH`; Android uses it to
decide that an APK is an update.

---

## 4. Installing the app (instructions for gym members)

You can send this section to members as is.

### Android

1. On the phone, open
   **https://github.com/Mohtashim890/IronForge/releases/latest/download/IronForge.apk**
   (or tap **IronForge.apk** on the release page). The download starts.
2. Open the downloaded file (from the notification or **Files** > **Downloads**).
3. The first time, Android says installing from this source is not allowed:
   tap **Settings**, turn on **Allow from this source** for your browser or
   Files app, and go back.
4. Tap **Install**, then **Open**.
5. Sign in with your gym account.

If Google Play Protect asks about an unknown app, choose **Install anyway**
(the app is not in the Play Store yet).

**Updating:** download and install the new `IronForge.apk` the same way. It
installs over the old one; you stay signed in and keep your data.

### Windows

1. Open
   **https://github.com/Mohtashim890/IronForge/releases/latest/download/IronForge-Setup.exe**
   (or click **IronForge-Setup.exe** on the release page).
2. Run the downloaded file.
3. If **Windows protected your PC** appears, click **More info**, then **Run
   anyway**. (The installer is not yet signed with a paid certificate, so
   Windows does not know the publisher.)
4. Follow the steps. No administrator rights are needed; the app installs for
   your account, and you can tick **Create a desktop shortcut**.
5. Start **IronForge** from the Start menu and sign in with your gym account.

**Updating:** run the new `IronForge-Setup.exe`. It replaces the old version
and keeps you signed in.

**Uninstalling:** **Settings** > **Apps** > **Installed apps** > **IronForge**
> **Uninstall**.

### Checking a download (optional)

Each file has a `.sha256` file on the release page with its checksum.

- Windows (PowerShell): `Get-FileHash .\IronForge-Setup.exe`
- Android APK on a computer: `Get-FileHash .\IronForge.apk` (or `sha256sum` on Linux/macOS)

The value must match the one in the `.sha256` file.

---

## 5. Troubleshooting

| Problem | Cause and fix |
|---|---|
| Android: **App not installed** / **package conflicts with an existing package** | The installed app was signed with another key, usually a Debug build from Visual Studio. Uninstall IronForge from the phone, then install the APK. Only happens once. |
| Android: **App not installed** when updating | The new version code is not higher than the installed one: release a higher version. |
| Release fails at **Restore keystore** | A secret is missing: section 1.2. |
| Release fails with **keystore password was incorrect** | `ANDROID_KEYSTORE_PASSWORD` must be only the first line of `signing-password.txt`. |
| Release fails with **signed with certificate …, expected …** | `ANDROID_KEYSTORE_BASE64` holds a different keystore than the one existing installs use: encode `ironforge-release.keystore` again (section 1.1). |
| Windows: SmartScreen warning | Expected without a code-signing certificate: **More info** > **Run anyway**. |
| Windows: installer refuses to run | The PC is 32-bit or ARM, or Windows is older than 10 (1809). The app is built for 64-bit Windows 10 1809+ / 11. |

Local builds of a release: run the same `dotnet publish` commands as the
workflow. A local Android build that failed at signing can leave an unsigned
`-Signed.apk` behind; delete `IronForge/bin/Release/net10.0-android` and
`IronForge/obj/Release/net10.0-android` before building again.

---

## 6. Later improvements

- **Windows code signing**, to remove the SmartScreen warning: a code-signing
  certificate (e.g. Microsoft Artifact Signing, if available in your country, or
  a commercial certificate), applied to `IronForge-Setup.exe` and
  `IronForge.exe` in the workflow.
- **Google Play**: the project already builds an AAB in Release; upload it to
  the Play Console (Internal testing first) for automatic updates and no
  "unknown source" step.
- **Update notice in the app**: compare the installed version with the latest
  release (GitHub API) and offer the download.
