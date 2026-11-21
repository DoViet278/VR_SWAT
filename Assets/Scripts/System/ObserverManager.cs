using System;
using System.Collections.Generic;

public static class ObserverManager
{
    private static Dictionary<string, CustomDelegate> NotificationList = new();

    public static void Register(string notificationName, Delegate action)
    {
        if (!NotificationList.TryAdd(
                notificationName,
                new CustomDelegate(
                    notificationName,
                    action
                )
            )
           )
        {
            NotificationList[notificationName] += action;
        }
    }

    public static void Unregister(string notificationName, Delegate action)
    {
        if (NotificationList.ContainsKey(notificationName))
        {
            NotificationList[notificationName] -= action;
        }
    }

    public static void InvokeEvent(string notificationName, params object[] values)
    {
        if (NotificationList.ContainsKey(notificationName))
        {
            NotificationList[notificationName]?.Invoke(values);
        }
    }

    public static void Clear(string notificationName)
    {
        if (NotificationList.ContainsKey(notificationName))
        {
            NotificationList[notificationName] = new CustomDelegate(notificationName);
        }
    }
}