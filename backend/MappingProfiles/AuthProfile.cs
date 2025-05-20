using AutoMapper;
using WalletBackend.Models;
using WalletBackend.Models.DTOS;

namespace WalletBackend.MappingProfiles;

public class AuthProfile: Profile
{
    public AuthProfile()
    {
        CreateMap<RegisterModel, ApplicationUser>()
            .ForMember(dest => dest.PasswordHash, opt => opt.MapFrom(src => src.Password));
        
        CreateMap<LoginModel, ApplicationUser>()
            .ForMember(dest => dest.PasswordHash, opt => opt.MapFrom(src => src.Password));
        
        // In your AutoMapper profile class

      

    }
}