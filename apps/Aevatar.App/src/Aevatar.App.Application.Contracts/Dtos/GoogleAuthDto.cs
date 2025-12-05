using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Aevatar.Dtos;

public class GoogleAuthVerifyCodeInput
{
    public string Platform { get; set; } = "web";
    [Required]
    public string Code { get; set; }
    [Required]
    public string RedirectUri { get; set; }

    public string CodeVerifier { get; set; } = string.Empty;
}