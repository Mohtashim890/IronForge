using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace IronForge.Shared.Configurations
{
    public class ApiSettings
    {
        public const string SectionName = "ApiSettings";

        [Required]
        [Url]
        public string BaseUrl { get; set; } = string.Empty;
    }
}
