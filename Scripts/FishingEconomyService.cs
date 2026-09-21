using System;
using System.Collections.Generic;

namespace FishingMod
{
    internal readonly struct FishingMoneyResult
    {
        internal FishingMoneyResult(bool recorded, float amount)
        {
            Recorded = recorded;
            Amount = amount;
        }

        internal bool Recorded { get; }
        internal float Amount { get; }
    }

    internal static class FishingEconomyService
    {
        internal const string SaleTransaction = "fishingmod_transaction_sale";
        internal const string LineBreakTransaction = "fishingmod_transaction_line_break";

        internal static FishingMoneyResult Settle(FishingQteSession session, Action<string> log)
        {
            if (session == null || !session.TryClaimSettlement(out FishingQteOutcome outcome)) return default;
            GameInstance save = SaveGameManager.Current;
            if (save == null || float.IsNaN(save.Money) || float.IsInfinity(save.Money))
            {
                log?.Invoke("[FishingMod] Money settlement skipped: no valid active balance.");
                return default;
            }

            float amount = outcome == FishingQteOutcome.Completed
                ? session.Fish.SalePrice
                : -FishingEconomyRules.LineBreakCost(save.Money);
            if (amount == 0f) return new FishingMoneyResult(true, 0f);

            TransactionInfo info = CreateTransactionInfo(session.Fish, outcome);
            try
            {
                // Native accounting updates the HUD, saved balance, history and money-change event.
                if (!GameManager.ChangeMoneySafe(amount, info, save.Day, force: false, showNotification: false))
                {
                    log?.Invoke("[FishingMod] Native money transaction was rejected.");
                    return default;
                }
            }
            catch (Exception exception)
            {
                // An event listener can throw after the native transaction was already committed.
                // Read its unique data reference back; never retry an uncertain payment.
                log?.Invoke("[FishingMod] Native money callback failed: " + exception.Message);
            }

            if (save.Transactions != null)
            {
                foreach (Transaction transaction in save.Transactions)
                {
                    if (transaction != null && ReferenceEquals(transaction.transactionData, info.Data))
                    {
                        log?.Invoke("[FishingMod] Recorded " + info.Type + " for " + session.Fish.Id
                            + ": " + transaction.amount.ToString("0.##") + ".");
                        return new FishingMoneyResult(true, transaction.amount);
                    }
                }
            }

            log?.Invoke("[FishingMod] Money transaction could not be confirmed; no retry will be attempted.");
            return default;
        }

        internal static TransactionInfo CreateTransactionInfo(FishingFish fish, FishingQteOutcome outcome)
        {
            if (fish == null) throw new ArgumentNullException(nameof(fish));
            if (outcome != FishingQteOutcome.Completed && outcome != FishingQteOutcome.Escaped)
                throw new ArgumentOutOfRangeException(nameof(outcome));
            // Recreational fishing is not casino income or a tax-deductible business expense.
            return new TransactionInfo(
                outcome == FishingQteOutcome.Completed ? SaleTransaction : LineBreakTransaction,
                new Dictionary<string, string> { { "fishId", fish.Id } });
        }
    }
}
