using Aevatar.App.LanguageManagement;
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
    }
}
