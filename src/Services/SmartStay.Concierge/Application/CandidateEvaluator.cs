using SmartStay.Concierge.Application.Ports;
using SmartStay.Concierge.Domain;
using SmartStay.Concierge.Infrastructure.Clients;
using SmartStay.Contracts.Http;

namespace SmartStay.Concierge.Application;

public class CandidateEvaluator(
    ILodgingSearch lodgingSearch,
    IBookingsClient bookingsClient,
    ILogger<CandidateEvaluator> logger) : ICandidateEvaluator
{
    public async Task<CandidateEvaluationResult> EvaluateCandidatesAsync(PurchaseIntent intent, CancellationToken ct = default)
    {
        var preferences = new StayPreferences(
            intent.CheckIn,
            intent.CheckOut,
            intent.Guests,
            intent.District,
            intent.BudgetTotal,
            intent.Currency
        );

        var searchResult = await lodgingSearch.SearchAsync(preferences, ct);

        if (!searchResult.AvailabilityServiceAvailable)
        {
            logger.LogWarning("Bookings availability service unavailable for intent {IntentId}.", intent.Id);
            return CandidateEvaluationResult.Failed("Availability service unavailable.");
        }

        if (searchResult.BudgetExceeded || searchResult.AvailableOffers.Count == 0)
        {
            logger.LogInformation("No eligible rooms within budget of ${BudgetTotal} for intent {IntentId}.", intent.BudgetTotal, intent.Id);
            return CandidateEvaluationResult.Failed("No available rooms within the specified budget.", budgetFailure: true);
        }

        // Ordenamiento determinista con desempate autoritativo:
        // TC_F17_01: Menor StayTotal primero
        // TC_F17_02: Menor RoomId como desempate estricto
        // TC_F17_04: Máximo 3 candidatos evaluados
        var candidates = searchResult.AvailableOffers
            .OrderBy(o => o.StayTotal)
            .ThenBy(o => o.RoomId)
            .Take(3)
            .ToList();

        for (int i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            logger.LogInformation("Evaluating candidate {Index}/3 for intent {IntentId}: Room #{RoomId} (StayTotal: ${StayTotal} USD)",
                i + 1, intent.Id, candidate.RoomId, candidate.StayTotal);

            var quoteRequest = new CreateBookingQuoteRequest(
                RoomId: candidate.RoomId,
                GuestUserId: intent.GuestUserId,
                PurchaseIntentId: intent.Id,
                CheckIn: intent.CheckIn.ToString("yyyy-MM-dd"),
                CheckOut: intent.CheckOut.ToString("yyyy-MM-dd"),
                Guests: intent.Guests
            );

            var quoteResult = await bookingsClient.RequestQuoteAsync(quoteRequest, ct);
            if (quoteResult.Success && quoteResult.Quote != null)
            {
                logger.LogInformation("Successfully obtained 2-minute quote {QuoteId} for Room #{RoomId}.",
                    quoteResult.Quote.QuoteId, candidate.RoomId);
                return CandidateEvaluationResult.Succeeded(candidate, quoteResult.Quote);
            }

            logger.LogWarning("Quote generation failed for candidate #{RoomId}: {Error}. Trying next candidate.",
                candidate.RoomId, quoteResult.ErrorMessage);
        }

        return CandidateEvaluationResult.Failed("All eligible room candidates (up to 3) are unavailable or quotes expired.");
    }
}
