namespace Aevatar.Application.Grains.Common.Constants;

[Orleans.GenerateSerializer]
public enum PaymentPlatform
{
    [Orleans.Id(0)] Stripe = 0,
    [Orleans.Id(1)] AppStore = 1,
    [Orleans.Id(2)] GooglePlay = 2
}
