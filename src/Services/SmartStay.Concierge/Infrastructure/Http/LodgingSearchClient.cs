using System.Net.Http.Json;
using SmartStay.Concierge.Application.Ports;
using SmartStay.Concierge.Domain;
using SmartStay.Contracts.Http;

namespace SmartStay.Concierge.Infrastructure.Http;

public class LodgingSearchClient(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<LodgingSearchClient> logger) : ILodgingSearch
{
    private readonly string _accommodationsUrl = configuration["Services:AccommodationsUrl"] ?? "http://accommodations:8080";
    private readonly string _bookingsUrl = configuration["Services:BookingsUrl"] ?? "http://bookings:8080";

    public async Task<IReadOnlyList<RoomOffer>> FindAsync(StayPreferences request, CancellationToken ct = default)
    {
        try
        {
            // 1. Consultar catálogo de habitaciones en Accommodations
            var searchUri = $"{_accommodationsUrl.TrimEnd('/')}/api/v1/rooms/search?district={Uri.EscapeDataString(request.District)}&guests={request.Guests}";
            var rooms = await httpClient.GetFromJsonAsync<List<RoomSearchResult>>(searchUri, ct) ?? [];

            if (rooms.Count == 0)
            {
                logger.LogInformation("No rooms matched criteria for district '{District}' with {Guests} guests.", request.District, request.Guests);
                return Array.Empty<RoomOffer>();
            }

            // 2. Consultar disponibilidad temporal orientativa en Bookings
            var roomIds = rooms.Select(r => r.RoomId).Distinct().Take(50).ToArray();
            var availRequest = new AvailabilityRequest(request.CheckIn, request.CheckOut, roomIds);

            var availUri = $"{_bookingsUrl.TrimEnd('/')}/internal/v1/availability/check";
            var availResponse = await httpClient.PostAsJsonAsync(availUri, availRequest, ct);

            HashSet<int> availableIds;
            if (availResponse.IsSuccessStatusCode)
            {
                var availResult = await availResponse.Content.ReadFromJsonAsync<AvailabilityResponse>(cancellationToken: ct);
                availableIds = new HashSet<int>(availResult?.AvailableRoomIds ?? []);
            }
            else
            {
                logger.LogWarning("Availability check failed with status {StatusCode}. Defaulting to all catalog items.", availResponse.StatusCode);
                availableIds = new HashSet<int>(roomIds);
            }

            var stayNights = Math.Max(1, request.CheckOut.DayNumber - request.CheckIn.DayNumber);

            var offers = rooms
                .Where(r => availableIds.Contains(r.RoomId) && r.IsBookable)
                .Select(r =>
                {
                    var stayTotal = stayNights * r.NightlyPrice;
                    return new RoomOffer(
                        RoomId: r.RoomId,
                        HotelId: r.HotelId,
                        HotelName: r.HotelName,
                        RoomTypeName: r.RoomTypeName,
                        District: r.District,
                        NightlyPrice: r.NightlyPrice,
                        StayTotal: stayTotal,
                        Currency: r.Currency,
                        Capacity: r.Capacity,
                        TourPath: r.TourPath,
                        Amenities: r.Amenities
                    );
                })
                .OrderBy(o => o.StayTotal <= request.BudgetTotal || request.BudgetTotal <= 0 ? 0 : 1)
                .ThenBy(o => o.StayTotal)
                .ThenBy(o => o.RoomId)
                .Take(3)
                .ToList();

            return offers;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while discovering room offers.");
            return Array.Empty<RoomOffer>();
        }
    }
}
