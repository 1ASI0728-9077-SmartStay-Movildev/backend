using SmartStay.IAM.Application.OutboundServices;
using SmartStay.IAM.Domain.Model.Aggregates;
using SmartStay.IAM.Domain.Model.Constants;
using SmartStay.IAM.Domain.Repositories;

namespace SmartStay.IAM.Infrastructure.Seed;

public class IamSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<IamSeeder>>();
        try
        {
            var config = services.GetRequiredService<IConfiguration>();
            var userRepository = services.GetRequiredService<IUserRepository>();
            var hashingService = services.GetRequiredService<IHashingService>();
            var unitOfWork = services.GetRequiredService<IIamUnitOfWork>();

            var defaultPassword = "Password123!";
            var defaultHash = hashingService.HashPassword(defaultPassword);

            // Baseline demo users in order so auto-increment produces IDs 1..5
            var demoUsers = new List<(string Username, string Role, int? HotelId, int? ChainId)>
            {
                ("admin", UserRoles.Admin, 1, 1),
                ("guest1", UserRoles.Guest, null, null),
                ("guest2", UserRoles.Guest, null, null),
                ("admin_cusco", UserRoles.Admin, 2, 1),
                ("chain_admin", UserRoles.ChainAdmin, null, 1)
            };

            var customAdminUser = config["InitialChainAdmin:Username"] ?? Environment.GetEnvironmentVariable("INITIAL_CHAIN_ADMIN_USERNAME");
            var customAdminPass = config["InitialChainAdmin:Password"] ?? Environment.GetEnvironmentVariable("INITIAL_CHAIN_ADMIN_PASSWORD");

            bool anyAdded = false;
            foreach (var seed in demoUsers)
            {
                if (await userRepository.ExistsByUsernameAsync(seed.Username))
                    continue;

                var pwdHash = (seed.Username == customAdminUser && !string.IsNullOrWhiteSpace(customAdminPass))
                    ? hashingService.HashPassword(customAdminPass)
                    : defaultHash;

                var user = new User(seed.Username, pwdHash, seed.Role, hotelId: seed.HotelId, chainId: seed.ChainId);
                await userRepository.AddAsync(user);
                anyAdded = true;
            }

            // Also support custom chain admin username if provided and distinct
            if (!string.IsNullOrWhiteSpace(customAdminUser) && !string.IsNullOrWhiteSpace(customAdminPass) && customAdminUser != "chain_admin")
            {
                if (!await userRepository.ExistsByUsernameAsync(customAdminUser))
                {
                    var user = new User(customAdminUser, hashingService.HashPassword(customAdminPass), UserRoles.ChainAdmin, hotelId: null, chainId: 1);
                    await userRepository.AddAsync(user);
                    anyAdded = true;
                }
            }

            if (anyAdded)
            {
                await unitOfWork.CompleteAsync();
                logger.LogInformation("IamSeeder: Seeded demo users successfully.");
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "IamSeeder: Seed failed.");
            throw;
        }
    }
}
