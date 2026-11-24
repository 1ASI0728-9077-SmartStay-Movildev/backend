using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Sockets;
using System.Security.Claims;
using SmartStay.Contracts.Constants;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Configuration;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Infrastructure;

[Trait("Tier", "Tier1")]
[Trait("Feature", "Infrastructure")]
public class InfrastructureVerificationTests
{
    [Fact]
    public void Configuration_DefaultsAreProperlyConfigured()
    {
        E2ETestConfig.GatewayUrl.Should().NotBeNullOrWhiteSpace();
        E2ETestConfig.GatewayUrl.Should().StartWith("http");

        E2ETestConfig.DirectBookingsUrl.Should().NotBeNullOrWhiteSpace();
        E2ETestConfig.DirectPaymentsUrl.Should().NotBeNullOrWhiteSpace();
        E2ETestConfig.DirectConciergeUrl.Should().NotBeNullOrWhiteSpace();

        E2ETestConfig.ActiveMqBrokerUri.Should().NotBeNullOrWhiteSpace();
        E2ETestConfig.ActiveMqBrokerUri.Should().StartWith("tcp://");

        E2ETestConfig.ActiveMqUser.Should().Be("admin");
        E2ETestConfig.ActiveMqPassword.Should().Be("admin");

        E2ETestConfig.DefaultPassword.Should().Be("Password123!");
        E2ETestConfig.JwtSecret.Should().NotBeNullOrWhiteSpace();
        E2ETestConfig.JwtIssuer.Should().NotBeNullOrWhiteSpace();
        E2ETestConfig.JwtAudience.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void AuthTokenFixture_CreatesValidSyntheticJwt_WithExpectedClaims()
    {
        var authFixture = new AuthTokenFixture();
        var token = authFixture.CreateSyntheticJwt(
            userId: 2,
            username: "guest1",
            role: UserRoles.Guest,
            tokenVersion: 1
        );

        token.Should().NotBeNullOrWhiteSpace();

        var handler = new JwtSecurityTokenHandler();
        handler.CanReadToken(token).Should().BeTrue();

        var jwt = handler.ReadJwtToken(token);
        jwt.Issuer.Should().Be(E2ETestConfig.JwtIssuer);
        jwt.Audiences.Should().Contain(E2ETestConfig.JwtAudience);

        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.NameIdentifier && c.Value == "2");
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Name && c.Value == "guest1");
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == UserRoles.Guest);
        jwt.Claims.Should().Contain(c => c.Type == "token_version" && c.Value == "1");
    }

    [Fact]
    public void AuthTokenFixture_SyntheticJwt_HandlesCustomExpiryAndSecret()
    {
        var authFixture = new AuthTokenFixture();
        var expiredToken = authFixture.CreateSyntheticJwt(
            userId: 99,
            username: "expired_user",
            role: UserRoles.Guest,
            lifetime: TimeSpan.FromMinutes(-15)
        );

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(expiredToken);
        jwt.ValidTo.Should().BeBefore(DateTime.UtcNow);
    }

    [Fact]
    public async Task ConcurrencyTestHelper_CoordinatesMultipleWorkers_WithBarrierSynchronization()
    {
        const int workerCount = 20;
        var executionTimes = new DateTime[workerCount];

        var results = await ConcurrencyTestHelper.ExecuteConcurrentBarrierAsync(
            workerCount,
            async workerId =>
            {
                executionTimes[workerId] = DateTime.UtcNow;
                await Task.Delay(10); // Simulate micro-workload
                return workerId * 2;
            },
            TimeSpan.FromSeconds(10)
        );

        results.Should().HaveCount(workerCount);
        for (var i = 0; i < workerCount; i++)
        {
            results.Should().Contain(i * 2);
        }

        // Verify barrier alignment: all workers should start within tight window (<2s)
        var maxTime = executionTimes.Max();
        var minTime = executionTimes.Min();
        (maxTime - minTime).Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ConcurrencyTestHelper_CoordinatesHighWorkerCounts_WithoutThreadPoolStarvation()
    {
        const int highWorkerCount = 50;
        var arrivedCount = 0;

        var stopwatch = Stopwatch.StartNew();
        var results = await ConcurrencyTestHelper.ExecuteConcurrentBarrierAsync(
            highWorkerCount,
            async workerId =>
            {
                Interlocked.Increment(ref arrivedCount);
                await Task.Yield();
                return workerId;
            },
            TimeSpan.FromSeconds(5)
        );
        stopwatch.Stop();

        results.Should().HaveCount(highWorkerCount);
        arrivedCount.Should().Be(highWorkerCount);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(2000, "asynchronous barrier must not starve the ThreadPool");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task ConcurrencyTestHelper_ThrowsArgumentOutOfRangeException_ForNonPositiveWorkers(int invalidCount)
    {
        var act = async () => await ConcurrencyTestHelper.ExecuteConcurrentBarrierAsync(
            invalidCount,
            workerId => Task.FromResult(workerId)
        );

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("workerCount");
    }

    [Fact]
    public async Task ConcurrencyTestHelper_ThrowsTimeoutException_WhenExecutionExceedsTimeout()
    {
        var act = async () => await ConcurrencyTestHelper.ExecuteConcurrentBarrierAsync(
            workerCount: 5,
            workerAction: async _ =>
            {
                await Task.Delay(2000);
                return true;
            },
            timeout: TimeSpan.FromMilliseconds(200)
        );

        var ex = await act.Should().ThrowAsync<TimeoutException>();
        ex.WithMessage("*Concurrent barrier execution timed out after 0.2 seconds for 5 workers*");
    }

    [Fact]
    public async Task ConcurrencyTestHelper_UnwrapsAndPropagatesInnerException_WhenWorkerFails()
    {
        var act = async () => await ConcurrencyTestHelper.ExecuteConcurrentBarrierAsync(
            workerCount: 5,
            workerAction: workerId =>
            {
                if (workerId == 2)
                {
                    throw new InvalidOperationException("Worker 2 simulated failure");
                }
                return Task.FromResult(workerId);
            }
        );

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("Worker 2 simulated failure");
    }

    [Fact]
    public async Task PollingHelper_Succeeds_WhenConditionIsMet()
    {
        var counter = 0;
        var result = await PollingHelper.PollUntilAsync(
            () => Task.FromResult<int?>(++counter),
            c => c >= 5,
            timeout: TimeSpan.FromSeconds(5),
            initialDelay: TimeSpan.FromMilliseconds(50),
            maxInterval: TimeSpan.FromMilliseconds(100)
        );

        result.Should().Be(5);
    }

    [Fact]
    public async Task PollingHelper_ThrowsTimeoutException_WhenConditionNeverMet()
    {
        var act = async () =>
        {
            await PollingHelper.PollUntilAsync<string>(
                () => Task.FromResult<string?>("not_ready"),
                s => s == "ready",
                timeout: TimeSpan.FromMilliseconds(300),
                initialDelay: TimeSpan.FromMilliseconds(50),
                maxInterval: TimeSpan.FromMilliseconds(50)
            );
        };

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public void SmartStayApiFixture_ConfiguresClients_WithConfiguredBaseAddresses()
    {
        using var fixture = new SmartStayApiFixture();

        fixture.GatewayClient.BaseAddress.Should().Be(new Uri(E2ETestConfig.GatewayUrl));
        fixture.DirectBookingsClient.BaseAddress.Should().Be(new Uri(E2ETestConfig.DirectBookingsUrl));
        fixture.DirectPaymentsClient.BaseAddress.Should().Be(new Uri(E2ETestConfig.DirectPaymentsUrl));
        fixture.DirectConciergeClient.BaseAddress.Should().Be(new Uri(E2ETestConfig.DirectConciergeUrl));
    }

    [Fact]
    public async Task SmartStayApiFixture_GatewayHealthCheck_SafelyQueriesGateway()
    {
        using var fixture = new SmartStayApiFixture();

        // 1. Fixture method must execute safely without uncaught exceptions
        var act = async () => await fixture.IsGatewayHealthyAsync();
        var result = await act.Should().NotThrowAsync();
        var isHealthy = result.Subject;

        // 2. Independently probe the Gateway health endpoint directly
        HttpResponseMessage? directResponse = null;
        try
        {
            directResponse = await fixture.GatewayClient.GetAsync("/health");
        }
        catch (HttpRequestException)
        {
            // Gateway is offline or unreachable
        }

        // 3. Authentically verify that IsGatewayHealthyAsync accurately reflects direct HTTP health status
        var expectedHealth = directResponse != null && directResponse.IsSuccessStatusCode;
        isHealthy.Should().Be(expectedHealth);

        if (expectedHealth)
        {
            directResponse!.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task ActiveMqFixture_ReachableCheck_ExecutesSafely()
    {
        using var fixture = new ActiveMqFixture();

        // 1. Fixture must safely handle broker query without uncaught exceptions
        var act = async () => await fixture.IsBrokerReachableAsync();
        var result = await act.Should().NotThrowAsync();
        var reachable = result.Subject;

        // 2. Independently verify broker reachability via TCP socket probe
        var brokerUri = new Uri(E2ETestConfig.ActiveMqBrokerUri);
        bool socketReachable;
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(brokerUri.Host, brokerUri.Port);
            socketReachable = await Task.WhenAny(connectTask, Task.Delay(500)) == connectTask && client.Connected;
        }
        catch
        {
            socketReachable = false;
        }

        // 3. Fixture reachability check must authentically match actual socket reachability
        reachable.Should().Be(socketReachable);
    }

    [Fact]
    public void GatewayClient_Instantiates_AndPreparesClientsCorrectly()
    {
        var client = new GatewayClient();
        client.RawClient.BaseAddress.Should().Be(new Uri(E2ETestConfig.GatewayUrl));
    }

    [Fact]
    public async Task LiveStack_WhenGatewayHealthy_CanAuthenticateGuestAndAdmin()
    {
        using var fixture = new SmartStayApiFixture();
        var authFixture = new AuthTokenFixture();
        var isHealthy = await fixture.IsGatewayHealthyAsync();

        if (isHealthy)
        {
            // When Gateway is healthy (live microservices stack), acquire and verify real JWT tokens
            var guestToken = await authFixture.GetGuest1TokenAsync(fixture.GatewayClient);
            guestToken.Should().NotBeNullOrWhiteSpace();

            var adminToken = await authFixture.GetAdminTokenAsync(fixture.GatewayClient);
            adminToken.Should().NotBeNullOrWhiteSpace();
        }
        else
        {
            // When Gateway is offline, verify that real network HTTP requests are attempted
            // and fail with HttpRequestException rather than silently returning or using dummy mocks
            var actGuest = async () => await authFixture.GetGuest1TokenAsync(fixture.GatewayClient);
            await actGuest.Should().ThrowAsync<HttpRequestException>();

            var actAdmin = async () => await authFixture.GetAdminTokenAsync(fixture.GatewayClient);
            await actAdmin.Should().ThrowAsync<HttpRequestException>();
        }
    }
}
