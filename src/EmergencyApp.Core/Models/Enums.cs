namespace EmergencyApp.Core.Models;

public enum ResponderType { Ambulance, FirstResponder }

public enum ResponderStatus { Offline, Available, EnRoute, Arrived }

public enum EmergencyStatus
{
    /// <summary>SOS received, no responder assigned yet (retried automatically).</summary>
    Pending,
    /// <summary>"Ambulance and responders are on a way".</summary>
    Dispatched,
    /// <summary>At least one responder reached the user.</summary>
    Arrived,
    Resolved,
    Cancelled
}

public enum MessageSender { User, Responder }

public enum EmergencyEventType
{
    Created,
    ResponderAssigned,
    ResponderLocationUpdated,
    ResponderArrived,
    MessageReceived,
    Cancelled,
    Resolved
}
