using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Models
{
    public class ServiceResult<T>
    {
        public bool Success { get; set; }

        public T? Data { get; set; }

        public string ErrorMessage { get; set; } = "";

        public int? StatusCode { get; set; }

        public static ServiceResult<T> Ok(T? data)
        {
            return new ServiceResult<T>
            {
                Success = true,
                Data = data
            };
        }

        public static ServiceResult<T> Fail(
            string? message,
            int? statusCode = null)
        {
            return new ServiceResult<T>
            {
                Success = false,
                ErrorMessage = message ?? "An error occurred.",
                StatusCode = statusCode
            };
        }
    }
}
