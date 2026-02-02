namespace Aevatar.App.Permissions;

public static class AppPermissions
{
    //public const string GroupName = "App";

    public const string GroupName = "Aevatar";
    public const string BasicUser = "basicUser";
    public const string DeveloperManager = "developerManager";
    public const string AdminGroup = "AdminManagement";
    public const string AdminPolicy = AdminGroup+".AdminPolicy";
    public const string DeveloperPlatform = "DeveloperPlatform";
    
    public const string OrganizationScopeKey = "Scope";
    
    // Permissions for Agent Management
    public static class Agent
    {
        public const string GroupName = "AgentManagement";
        public const string ViewLogs = GroupName + ".ViewLogs";
        public const string ViewAllType = GroupName + ".ViewAllType";
        public const string ViewList = GroupName + ".ViewList";
        public const string Create = GroupName + ".Create";
        public const string View = GroupName + ".View";
        public const string Update = GroupName + ".Update";
        public const string Delete = GroupName + ".Delete";
    }

    // Permissions for Relationship Management
    public static class Relationship
    {
        public const string GroupName = "AgentRelationshipManagement";

        public const string ViewRelationship = GroupName + ".View";
        public const string AddSubAgent = GroupName + ".AddSubAgent";
        public const string RemoveSubAgent = GroupName + ".RemoveSubAgent";
        public const string RemoveAllSubAgents = GroupName + ".RemoveAllSubAgents";
    }

    // Permissions for Event Management
    public static class EventManagement
    {
        public const string GroupName = "EventManagement";

        public const string Publish = GroupName + ".Publish";
        public const string View = GroupName + ".View"; 
    }
    
    public static class HostManagement
    {
        public const string GroupName = "HostManagement"; 
        public const string Logs = GroupName + ".ViewLogs"; 
    }
    
    public static class CqrsManagement
    {
        public const string GroupName = "CqrsManagement"; 
        public const string Logs = GroupName + ".ViewLogs"; 
        public const string States = GroupName + ".ViewStates"; 
    }
    
    public static class SubscriptionManagent
    {
        public const string GroupName = "SubscriptionManagement"; 
        
        public const string CreateSubscription = GroupName + ".CreateSubscription"; 
        public const string CancelSubscription = GroupName + ".CancelSubscription"; 
        public const string ViewSubscriptionStatus = GroupName + ".ViewSubscription"; 
    }
    
    public static class Organizations
    {
        public const string Default = DeveloperPlatform + ".Organizations";
        public const string Create = Default + ".Create";
        public const string Edit = Default + ".Edit";
        public const string Delete = Default + ".Delete";
    }
    
    public static class Members
    {
        public const string Default = DeveloperPlatform + ".Members";
        public const string Manage = Default + ".Manage";
    }
    
    public static class Projects
    {
        public const string Default = DeveloperPlatform + ".Projects";
        public const string Create = Default + ".Create";
        public const string Edit = Default + ".Edit";
        public const string Delete = Default + ".Delete";
    }
    
    public static class ApiKeys
    {
        public const string Default = DeveloperPlatform + ".ApiKeys";
        public const string Create = Default + ".Create";
        public const string Edit = Default + ".Edit";
        public const string Delete = Default + ".Delete";
    }
    
    public static class Roles
    {
        public const string Default = DeveloperPlatform + ".Roles";
        public const string Create = Default + ".Create";
        public const string Edit = Default + ".Edit";
        public const string Delete = Default + ".Delete";
    }
    
    public const string Dashboard = DeveloperPlatform + ".Dashboards";
    
    public static class LLMSModels
    {
        public const string Default = Dashboard + ".LLMSModels";
    }
    
    public static class ApiRequests
    {
        public const string Default = Dashboard + ".ApiRequests";
    }
    
    /// <summary>
    /// Permissions for Language Management module (first level).
    /// </summary>
    public static class LanguageManagement
    {
        public const string GroupName = "LanguageManagement";
        
        /// <summary>
        /// Permissions for Languages (second level - LanguageController).
        /// </summary>
        public static class Languages
        {
            public const string Default = GroupName + ".Languages";
            public const string Create = Default + ".Create";
            public const string Edit = Default + ".Edit";
            public const string Delete = Default + ".Delete";
        }
        
        /// <summary>
        /// Permissions for Language Texts (second level - LanguageTextController).
        /// </summary>
        public static class LanguageTexts
        {
            public const string Default = GroupName + ".LanguageTexts";
            public const string Create = Default + ".Create";
            public const string Edit = Default + ".Edit";
            public const string Restore = Default + ".Restore";
        }
    }

    /// <summary>
    /// Permissions for Push Notification Management module.
    /// </summary>
    public static class PushNotification
    {
        public const string GroupName = "PushNotificationManagement";
        
        /// <summary>
        /// Base permission for push notification management.
        /// </summary>
        public const string Default = GroupName + ".Default";
        
        /// <summary>
        /// Permission to send push notifications by timezone.
        /// </summary>
        public const string SendByTimezone = GroupName + ".SendByTimezone";
    }
}
