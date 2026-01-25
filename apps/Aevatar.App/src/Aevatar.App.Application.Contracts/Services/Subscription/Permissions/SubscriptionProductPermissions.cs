namespace Aevatar.App.Services.Subscription.Permissions;

/// <summary>
/// Permission constants for subscription product management.
/// </summary>
public static class SubscriptionProductPermissions
{
    public const string GroupName = "SubscriptionProductManagement";
    
    /// <summary>
    /// Product management permissions
    /// </summary>
    public static class Products
    {
        public const string Default = GroupName + ".Products";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string SetListed = Default + ".SetListed";
    }
    
    /// <summary>
    /// Label management permissions
    /// </summary>
    public static class Labels
    {
        public const string Default = GroupName + ".Labels";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }
    
    /// <summary>
    /// Feature management permissions
    /// </summary>
    public static class Features
    {
        public const string Default = GroupName + ".Features";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string Reorder = Default + ".Reorder";
    }
    
    /// <summary>
    /// Price management permissions
    /// </summary>
    public static class Prices
    {
        public const string Default = GroupName + ".Prices";
        public const string Set = Default + ".Set";
        public const string Delete = Default + ".Delete";
        public const string SyncPrice = Default + ".SyncPrice";
    }
}
