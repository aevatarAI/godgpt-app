using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace Aevatar.App.Domain.Data;

/// <summary>
/// Data seed contributor to create systemPromptManager role and assign it to admin user
/// </summary>
public class SystemPromptManagerRoleSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly IIdentityUserRepository _userRepository;
    private readonly IdentityUserManager _userManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ILogger<SystemPromptManagerRoleSeedContributor> _logger;

    public SystemPromptManagerRoleSeedContributor(
        IIdentityRoleRepository roleRepository,
        IIdentityUserRepository userRepository,
        IdentityUserManager userManager,
        IGuidGenerator guidGenerator,
        ILogger<SystemPromptManagerRoleSeedContributor> logger)
    {
        _roleRepository = roleRepository;
        _userRepository = userRepository;
        _userManager = userManager;
        _guidGenerator = guidGenerator;
        _logger = logger;
    }

    [UnitOfWork]
    public async Task SeedAsync(DataSeedContext context)
    {
        _logger.LogInformation("Seeding systemPromptManager role...");

        // Create systemPromptManager role if it doesn't exist
        var roleName = "systemPromptManager";
        var role = await _roleRepository.FindByNormalizedNameAsync(roleName.ToUpperInvariant());
        
        if (role == null)
        {
            role = new IdentityRole(
                _guidGenerator.Create(),
                roleName,
                context.TenantId
            )
            {
                IsPublic = true,
                IsDefault = false,
                IsStatic = false
            };

            await _roleRepository.InsertAsync(role);
            _logger.LogInformation("Created systemPromptManager role");
        }
        else
        {
            _logger.LogInformation("systemPromptManager role already exists");
        }

        // Find admin user (by username or email)
        var adminUser = await _userRepository.FindByNormalizedUserNameAsync("ADMIN");
        if (adminUser == null)
        {
            // Try to find by email
            adminUser = await _userRepository.FindByNormalizedEmailAsync("ADMIN@ABP.IO");
        }

        if (adminUser != null)
        {
            // Check if admin user already has the role
            var userRoles = await _userManager.GetRolesAsync(adminUser);
            if (!userRoles.Contains(roleName))
            {
                await _userManager.AddToRoleAsync(adminUser, roleName);
                _logger.LogInformation("Assigned systemPromptManager role to admin user: {UserId}", adminUser.Id);
            }
            else
            {
                _logger.LogInformation("Admin user already has systemPromptManager role");
            }
        }
        else
        {
            _logger.LogWarning("Admin user not found, cannot assign systemPromptManager role");
        }
    }
}

