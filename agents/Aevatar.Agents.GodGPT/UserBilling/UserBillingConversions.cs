using Aevatar.Agents.GodGPT.Protos.UserBilling;
using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.ChatManager.UserBilling.Payment;
using Aevatar.Application.Grains.Common.Constants;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Application.Grains.UserBilling;

/// <summary>
/// Conversion helpers for UserBillingGAgent Protobuf types
/// </summary>
public static class UserBillingConversions
{
    // =============================================================================
    // Timestamp Helpers
    // =============================================================================
    
 

    public static Timestamp? ToTimestampNullable(this DateTime? dateTime)
    {
        return dateTime.HasValue 
            ? Timestamp.FromDateTime(DateTime.SpecifyKind(dateTime.Value, DateTimeKind.Utc)) 
            : null;
    }

    public static DateTime? ToDateTimeNullable(this Timestamp? timestamp)
    {
        return timestamp?.ToDateTime();
    }

    // =============================================================================
    // PaymentSummary Conversions
    // =============================================================================

    public static PaymentSummaryProto ToProto(this PaymentSummary summary)
    {
        var proto = new PaymentSummaryProto
        {
            PaymentGrainId = summary.PaymentGrainId.ToString(),
            OrderId = summary.OrderId ?? "",
            PlanType = (QuotaPlanType)summary.PlanType,
            Amount = (double)summary.Amount,
            Currency = summary.Currency ?? "USD",
            CreatedAt = summary.CreatedAt.ToProtoTimestamp(),
            Status = (QuotaPaymentStatus)summary.Status,
            PaymentType = (int)summary.PaymentType,
            Method = (int)summary.Method,
            Platform = (int)summary.Platform,
            IsSubscriptionRenewal = summary.IsSubscriptionRenewal,
            SubscriptionId = summary.SubscriptionId ?? "",
            SubscriptionStartDate = summary.SubscriptionStartDate.ToProtoTimestamp(),
            SubscriptionEndDate = summary.SubscriptionEndDate.ToProtoTimestamp(),
            SessionId = summary.SessionId ?? "",
            UserId = summary.UserId.ToString(),
            PriceId = summary.PriceId ?? "",
            AppStoreEnvironment = summary.AppStoreEnvironment ?? "",
            MembershipLevel = summary.MembershipLevel ?? ""
        };

        if (summary.CompletedAt.HasValue)
        {
            proto.CompletedAt = summary.CompletedAt.Value.ToProtoTimestamp();
        }

        if (summary.AmountNetTotal.HasValue)
        {
            proto.AmountNetTotal = (double)summary.AmountNetTotal.Value;
        }

        if (summary.InvoiceDetails != null)
        {
            foreach (var invoice in summary.InvoiceDetails)
            {
                proto.InvoiceDetails.Add(invoice.ToProto());
            }
        }

        return proto;
    }

    public static PaymentSummary FromProto(this PaymentSummaryProto proto)
    {
        return new PaymentSummary
        {
            PaymentGrainId = Guid.Parse(proto.PaymentGrainId),
            OrderId = proto.OrderId,
            PlanType = (PlanType)proto.PlanType,
            Amount = (decimal)proto.Amount,
            Currency = proto.Currency,
            CreatedAt = proto.CreatedAt.ToDateTime(),
            CompletedAt = proto.CompletedAt?.ToDateTime(),
            Status = (PaymentStatus)proto.Status,
            PaymentType = (PaymentType)proto.PaymentType,
            Method = (PaymentMethod)proto.Method,
            Platform = (PaymentPlatform)proto.Platform,
            IsSubscriptionRenewal = proto.IsSubscriptionRenewal,
            SubscriptionId = proto.SubscriptionId,
            SubscriptionStartDate = proto.SubscriptionStartDate.ToDateTime(),
            SubscriptionEndDate = proto.SubscriptionEndDate.ToDateTime(),
            SessionId = proto.SessionId,
            UserId = Guid.Parse(proto.UserId),
            PriceId = proto.PriceId,
            InvoiceDetails = proto.InvoiceDetails?.Select(i => i.FromProto()).ToList() ?? new List<UserBillingInvoiceDetail>(),
            AppStoreEnvironment = proto.AppStoreEnvironment,
            MembershipLevel = proto.MembershipLevel,
            AmountNetTotal = proto.HasAmountNetTotal ? (decimal?)proto.AmountNetTotal : null
        };
    }

    public static List<PaymentSummary> FromProtoList(this Google.Protobuf.Collections.RepeatedField<PaymentSummaryProto> protos)
    {
        return protos?.Select(p => p.FromProto()).ToList() ?? new List<PaymentSummary>();
    }

    // =============================================================================
    // UserBillingInvoiceDetail Conversions
    // =============================================================================

    public static UserBillingInvoiceDetailProto ToProto(this UserBillingInvoiceDetail detail)
    {
        var proto = new UserBillingInvoiceDetailProto
        {
            InvoiceId = detail.InvoiceId ?? "",
            CreatedAt = detail.CreatedAt.ToProtoTimestamp(),
            CompletedAt = detail.CompletedAt.ToProtoTimestamp(),
            Status = (QuotaPaymentStatus)detail.Status,
            SubscriptionStartDate = detail.SubscriptionStartDate.ToProtoTimestamp(),
            SubscriptionEndDate = detail.SubscriptionEndDate.ToProtoTimestamp(),
            PriceId = detail.PriceId ?? "",
            MembershipLevel = detail.MembershipLevel ?? "",
            PlanType = (QuotaPlanType)detail.PlanType,
            PurchaseToken = detail.PurchaseToken ?? "",
            IsTrial = detail.IsTrial,
            TrialCode = detail.TrialCode ?? ""
        };

        if (detail.Amount.HasValue)
        {
            proto.Amount = (double)detail.Amount.Value;
        }

        if (!string.IsNullOrEmpty(detail.Currency))
        {
            proto.Currency = detail.Currency;
        }

        if (detail.AmountNetTotal.HasValue)
        {
            proto.AmountNetTotal = (double)detail.AmountNetTotal.Value;
        }

        if (detail.Discounts != null)
        {
            foreach (var discount in detail.Discounts)
            {
                proto.Discounts.Add(discount.ToProto());
            }
        }

        return proto;
    }

    public static UserBillingInvoiceDetail FromProto(this UserBillingInvoiceDetailProto proto)
    {
        return new UserBillingInvoiceDetail
        {
            InvoiceId = proto.InvoiceId,
            CreatedAt = proto.CreatedAt.ToDateTime(),
            CompletedAt = proto.CompletedAt.ToDateTime(),
            Status = (PaymentStatus)proto.Status,
            SubscriptionStartDate = proto.SubscriptionStartDate.ToDateTime(),
            SubscriptionEndDate = proto.SubscriptionEndDate.ToDateTime(),
            PriceId = proto.PriceId,
            MembershipLevel = proto.MembershipLevel,
            Amount = proto.HasAmount ? (decimal?)proto.Amount : null,
            PlanType = (PlanType)proto.PlanType,
            PurchaseToken = proto.PurchaseToken,
            Currency = proto.HasCurrency ? proto.Currency : null,
            AmountNetTotal = proto.HasAmountNetTotal ? (decimal?)proto.AmountNetTotal : null,
            Discounts = proto.Discounts?.Select(d => d.FromProto()).ToList(),
            IsTrial = proto.IsTrial,
            TrialCode = proto.TrialCode
        };
    }

    // =============================================================================
    // DiscountDetails Conversions
    // =============================================================================

    public static DiscountDetailsProto ToProto(this DiscountDetails detail)
    {
        var proto = new DiscountDetailsProto
        {
            DiscountId = detail.DiscountId ?? "",
            CouponId = detail.CouponId ?? "",
            CouponName = detail.CouponName ?? "",
            PromotionCodeId = detail.PromotionCodeId ?? "",
            PromotionCode = detail.PromotionCode ?? ""
        };

        if (detail.AmountOff.HasValue)
        {
            proto.AmountOff = detail.AmountOff.Value;
        }

        if (detail.PercentOff.HasValue)
        {
            proto.PercentOff = (double)detail.PercentOff.Value;
        }

        return proto;
    }

    public static DiscountDetails FromProto(this DiscountDetailsProto proto)
    {
        return new DiscountDetails
        {
            DiscountId = proto.DiscountId,
            CouponId = proto.CouponId,
            CouponName = proto.CouponName,
            AmountOff = proto.HasAmountOff ? proto.AmountOff : null,
            PercentOff = proto.HasPercentOff ? (decimal?)proto.PercentOff : null,
            PromotionCodeId = proto.PromotionCodeId,
            PromotionCode = proto.PromotionCode
        };
    }

    // =============================================================================
    // State PaymentHistory Helpers
    // =============================================================================

    public static PaymentSummary? GetPaymentById(
        this Google.Protobuf.Collections.RepeatedField<PaymentSummaryProto> paymentHistory, 
        Guid paymentId)
    {
        var proto = paymentHistory.FirstOrDefault(p => p.PaymentGrainId == paymentId.ToString());
        return proto?.FromProto();
    }

    public static PaymentSummaryProto? GetPaymentProtoById(
        this Google.Protobuf.Collections.RepeatedField<PaymentSummaryProto> paymentHistory,
        Guid paymentId)
    {
        return paymentHistory.FirstOrDefault(p => p.PaymentGrainId == paymentId.ToString());
    }

    public static PaymentSummary? GetPaymentBySubscriptionId(
        this Google.Protobuf.Collections.RepeatedField<PaymentSummaryProto> paymentHistory,
        string subscriptionId)
    {
        var proto = paymentHistory.FirstOrDefault(p => p.SubscriptionId == subscriptionId);
        return proto?.FromProto();
    }

    public static PaymentSummaryProto? GetPaymentProtoBySubscriptionId(
        this Google.Protobuf.Collections.RepeatedField<PaymentSummaryProto> paymentHistory,
        string subscriptionId)
    {
        return paymentHistory.FirstOrDefault(p => p.SubscriptionId == subscriptionId);
    }
}

