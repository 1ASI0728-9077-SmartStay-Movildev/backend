using SmartStay.Concierge.Domain;
using SmartStay.Contracts.Http;

namespace SmartStay.Concierge.Application;

public sealed record CandidateEvaluationResult(
    bool Success,
    RoomOffer? SelectedCandidate,
    BookingQuoteResponse? Quote,
    bool ImmediateBudgetFailure,
    string? FailureReason
)
{
    public static CandidateEvaluationResult Succeeded(RoomOffer candidate, BookingQuoteResponse quote) =>
        new(true, candidate, quote, false, null);

    public static CandidateEvaluationResult Failed(string reason, bool budgetFailure = false) =>
        new(false, null, null, budgetFailure, reason);
}

public interface ICandidateEvaluator
{
    Task<CandidateEvaluationResult> EvaluateCandidatesAsync(PurchaseIntent intent, CancellationToken ct = default);
}
