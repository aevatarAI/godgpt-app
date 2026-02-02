using System;
using Aevatar.Agents.GodGPT.Protos.UserDevice;
using Aevatar.App.LanguageManagement;
using Aevatar.Dtos.Push;
using AutoMapper;

namespace Aevatar.App;

public class AppApplicationAutoMapperProfile : Profile
{
    public AppApplicationAutoMapperProfile()
    {
        /* You can configure your AutoMapper mapping configuration here.
         * Alternatively, you can split your mapping configurations
         * into multiple profile classes for a better organization. */
        
        CreateMap<Language, LanguageDto>();
        
        // Push notification mappings
        CreateMap<DeviceInfo, DeviceInfoDto>()
            .ForMember(dest => dest.TokenUpdatedAt, opt => opt.MapFrom(src => src.TokenUpdatedAt != null ? src.TokenUpdatedAt.ToDateTime() : (DateTime?)null))
            .ForMember(dest => dest.LastActiveAt, opt => opt.MapFrom(src => src.LastActiveAt != null ? src.LastActiveAt.ToDateTime() : (DateTime?)null));
    }
}
