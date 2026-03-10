namespace Aevatar.Payment.Providers;

internal static class PaymentPeriodEndHelper
{
    public static DateTime? MaxPeriodEnd(DateTime? calculatedPeriodEnd, DateTime? platformPeriodEnd)
    {
        if (calculatedPeriodEnd.HasValue && platformPeriodEnd.HasValue)
        {
            return calculatedPeriodEnd.Value >= platformPeriodEnd.Value
                ? calculatedPeriodEnd.Value
                : platformPeriodEnd.Value;
        }

        return calculatedPeriodEnd ?? platformPeriodEnd;
    }
}
