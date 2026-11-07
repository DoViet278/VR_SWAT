namespace VRSwat.Doors
{
    public enum DoorState
    {
        Open,
        Closed,
        Locked,
        Breached
    };

    public enum DoorAction
    {
        Open,
        Close,
        Breach
    };

    public enum DoorRequestResult
    {
        Accepted,
        Refused,
        Busy
    }
}