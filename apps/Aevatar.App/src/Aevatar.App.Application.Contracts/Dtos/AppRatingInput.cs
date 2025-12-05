using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Aevatar.Dtos;

public class RecordAppRatingInput
{
    [Required]
    //iOS、Android
    public string Platform { get; set; }
    [Required]
    public string DeviceId { get; set; }
}

public class CanUserRateAppInput
{
    [Required]
    public string DeviceId { get; set; }
}