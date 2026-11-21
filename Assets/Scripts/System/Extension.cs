using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

public static class Extension
{
    #region CoreTemplate

    public static T ToObject<T>(this string data)
    {
        return JsonConvert.DeserializeObject<T>(data);
    }

    public static string ToJson(this object data)
    {
        return JsonConvert.SerializeObject(data);
    }

    public static RectTransform rect(this Component c)
    {
        return c.GetComponent<RectTransform>();
    }

    public static string GetFullNameMethod(this Delegate d)
    {
        return $"{d.Method.DeclaringType}.{d.Method.Name}";
    }

    public static IEnumerable<T> GetValues<T>() where T : Enum
    {
        return Enum.GetValues(typeof(T)).Cast<T>();
    }

    #endregion
}