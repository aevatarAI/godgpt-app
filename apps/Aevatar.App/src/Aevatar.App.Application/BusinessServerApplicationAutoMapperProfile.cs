using Aevatar.App.LanguageManagement;
using Aevatar.App.Services.Terms;
using Aevatar.App.Terms;
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
        
        // Terms of Service mappings
        CreateMap<TermsOfServiceVersion, TermsVersionDto>();

    }
}
