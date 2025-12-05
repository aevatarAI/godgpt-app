using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Aevatar.Application.Grains.Common.Constants;

namespace Aevatar.GodGPT.Dtos;

public class RedeemInviteCodeRequest
{
    [Required]
    [StringLength(20)]
    public string InviteCode { get; set; }

    public bool IsWeb { get; set; } = false;
}

public class RedeemInviteCodeResponse
{
    public bool IsValid { get; set; }
    public InvitationCodeType CodeType { get; set; }
    public string URL { get; set; }
}

public class GetInvitationCodeTypeRequest
{
    [Required]
    [StringLength(20)]
    public string InviteCode { get; set; }
}

public class GetInvitationCodeTypeResponse
{
    public InvitationCodeType CodeType { get; set; }
}