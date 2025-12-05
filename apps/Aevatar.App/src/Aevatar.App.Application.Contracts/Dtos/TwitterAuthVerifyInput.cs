using System;
using System.Collections.Generic;
namespace Aevatar.GodGPT.Dtos;

public class TwitterAuthVerifyInput
{
    public string Code { get; set; }
    public string Platform { get; set; } = "Web";
    public string RedirectUri { get; set; }
}