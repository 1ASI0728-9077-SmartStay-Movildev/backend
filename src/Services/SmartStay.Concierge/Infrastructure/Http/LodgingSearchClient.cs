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

    public async Task<LodgingSearchResult> SearchAsync(StayPreferences request, CancellationToken ct = default)
    {
        try
        {
            // 1. Consultar catálogo de habitaciones en Accommodations
            var searchUri = $"{_accommodationsUrl.TrimEnd('/')}/api/v1/rooms/search?district={Uri.EscapeDataString(request.District)}&guests={request.Guests}";
            List<RoomSearchResult> catalogRooms;

            try
            {
                catalogRooms = await httpClient.GetFromJsonAsync<List<RoomSearchResult>>(searchUri, ct) ?? [];
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to query Accommodations catalog service at {Uri}.", searchUri);
                return new LodgingSearchResult([], AvailabilityServiceAvailable: false, BudgetExceeded: false, 0, 0);
            }

            if (catalogRooms.Count == 0)
            {
                logger.LogInformation("No rooms in catalog matched district '{District}' with {Guests} guests.", request.District, request.Guests);
                return new LodgingSearchResult([], AvailabilityServiceAvailable: true, BudgetExceeded: false, 0, 0);
            }

            // Filtrar estrictamente por la moneda oficial del MVP (USD) y condición de reservable
            var eligibleRooms = catalogRooms
                .Where(r => r.IsBookable && string.Equals(r.Currency, "USD", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (eligibleRooms.Count == 0)
            {
                return new LodgingSearchResult([], AvailabilityServiceAvailable: true, BudgetExceeded: false, 0, catalogRooms.Count);
            }

            // 2. Consultar disponibilidad temporal autoritativa en Bookings
            var roomIds = eligibleRooms.Select(r => r.RoomId).Distinct().Take(50).ToArray();
            var availRequest = new AvailabilityRequest(request.CheckIn, request.CheckOut, roomIds);
            var availUri = $"{_bookingsUrl.TrimEnd('/')}/internal/v1/availability/check";

            HashSet<int> availableIds;
            try
            {
                var availResponse = await httpClient.PostAsJsonAsync(availUri, availRequest, ct);
                if (!availResponse.IsSuccessStatusCode)
                {
                    logger.LogWarning("Bookings availability check failed with status {StatusCode}. Availability is UNKNOWN.", availResponse.StatusCode);
                    return new LodgingSearchResult([], AvailabilityServiceAvailable: false, BudgetExceeded: false, 0, catalogRooms.Count);
                }

                var availResult = await availResponse.Content.ReadFromJsonAsync<AvailabilityResponse>(cancellationToken: ct);
                availableIds = new HashSet<int>(availResult?.AvailableRoomIds ?? []);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to connect to Bookings availability service at {Uri}.", availUri);
                return new LodgingSearchResult([], AvailabilityServiceAvailable: false, BudgetExceeded: false, 0, catalogRooms.Count);
            }

            var stayNights = Math.Max(1, request.CheckOut.DayNumber - request.CheckIn.DayNumber);

            var availableRooms = eligibleRooms
                .Where(r => availableIds.Contains(r.RoomId))
                .Select(r => new RoomOffer(
                    RoomId: r.RoomId,
                    HotelId: r.HotelId,
                    HotelName: r.HotelName,
                    RoomTypeName: r.RoomTypeName,
                    District: r.District,
                    NightlyPrice: r.NightlyPrice,
                    StayTotal: stayNights * r.NightlyPrice,
                    Currency: "USD",
                    Capacity: r.Capacity,
                    TourPath: r.TourPath,
                    Amenities: r.Amenities
                ))
                .ToList();

            if (availableRooms.Count == 0)
            {
                return new LodgingSearchResult([], AvailabilityServiceAvailable: true, BudgetExceeded: false, 0, catalogRooms.Count);
            }

            var minAvailableTotal = availableRooms.Min(r => r.StayTotal);

            // 3. Filtrar estrictamente por presupuesto si el usuario especificó uno
            if (request.BudgetTotal > 0)
            {
                var withinBudget = availableRooms.Where(r => r.StayTotal <= request.BudgetTotal).ToList();
                if (withinBudget.Count == 0)
                {
                    logger.LogInformation("All {Count} available rooms exceed the user budget of ${BudgetTotal} USD. Lowest available is ${Lowest}.",
                        availableRooms.Count, request.BudgetTotal, minAvailableTotal);
                    return new LodgingSearchResult([], AvailabilityServiceAvailable: true, BudgetExceeded: true, minAvailableTotal, catalogRooms.Count);
                }

                availableRooms = withinBudget;
            }

            var finalOffers = availableRooms
                .OrderBy(o => o.StayTotal)
                .ThenBy(o => o.RoomId)
                .Take(3)
                .ToList();

            return new LodgingSearchResult(finalOffers, AvailabilityServiceAvailable: true, BudgetExceeded: false, minAvailableTotal, catalogRooms.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error during lodging search execution.");
            return new LodgingSearchResult([], AvailabilityServiceAvailable: false, BudgetExceeded: false, 0, 0);
        }
    }
}
