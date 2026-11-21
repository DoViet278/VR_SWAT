using System;
using System.Linq;
using UnityEngine;

public class CustomDelegate
{
    private readonly string _name;
    private Delegate _action;

    public CustomDelegate(string name, params Delegate[] actions)
    {
        _name = name;
        _action = null;
        foreach (var action in actions)
        {
            AddNewAction(action);
        }
    }

    public void Invoke(params object[] parameters)
    {
        if (_action == null) return;
        var existingParamCount = _action.Method.GetParameters().Length;
        var newActionParamCount = parameters.Length;

        if (existingParamCount != newActionParamCount)
        {
            Debug.LogError(
                $"Event {_name} : The number of parameters does not match. " +
                $"{_name} has {existingParamCount} parameter with first method is " +
                $"{_action.GetInvocationList()[0].GetFullNameMethod()} " +
                $"while invoke has {newActionParamCount} parameter."
            );
            return;
        }

        foreach (var action in _action.GetInvocationList())
        {
            try
            {
                action?.DynamicInvoke(parameters);
            }
            catch (Exception e)
            {
                if (action != null)
                    Debug.LogWarning(
                        $"Event {_name} : error : " +
                        $"{action.GetFullNameMethod()} => {e}"
                    );
            }
        }
    }

    private CustomDelegate AddNewAction(Delegate action)
    {
        if (_action == null || _action.GetInvocationList().Length <= 0)
        {
            _action = action;
            return this;
        }

        var existingParamCount = _action.Method.GetParameters().Length;
        var newActionParamCount = action.Method.GetParameters().Length;

        if (existingParamCount != newActionParamCount)
        {
            Debug.LogError(
                $"Event {_name} : The number of parameters does not match. " +
                $"{_name} has {existingParamCount} parameter with first method is " +
                $"{_action.GetInvocationList()[0].GetFullNameMethod()}, " +
                $"while {action.GetFullNameMethod()} has {newActionParamCount} parameter."
            );
            return this;
        }

        if (_action.GetInvocationList().Contains(action))
        {
            Debug.LogError(
                $"Event {_name} : Action already exists : " +
                $"{action.GetFullNameMethod()}"
            );
            return this;
        }

        _action = Delegate.Combine(_action, action);
        return this;
    }

    public static CustomDelegate operator +(CustomDelegate e, Delegate action)
    {
        return e.AddNewAction(action);
    }

    public static CustomDelegate operator -(CustomDelegate e, Delegate action)
    {
        if (e._action == null || e._action.GetInvocationList().Length <= 0)
        {
            Debug.LogWarning(
                $"Event {e._name} : Event is null or empty."
            );
            return e;
        }

        var existingParamCount = e._action.Method.GetParameters().Length;
        var newActionParamCount = action.Method.GetParameters().Length;

        if (existingParamCount != newActionParamCount)
        {
            Debug.LogWarning(
                $"Event {e._name} : The number of parameters does not match. " +
                $"{e._name} has {existingParamCount} parameter with first method is " +
                $"{e._action.GetInvocationList()[0].GetFullNameMethod()}, " +
                $"while {action.GetFullNameMethod()} has {newActionParamCount} parameter."
            );
            return e;
        }

        if (!e._action.GetInvocationList().Contains(action))
        {
            Debug.LogWarning(
                $"Event {e._name} : Action does not exist : " +
                $"{action.GetFullNameMethod()}"
            );
            return e;
        }

        e._action = Delegate.Remove(e._action, action);
        return e;
    }
}