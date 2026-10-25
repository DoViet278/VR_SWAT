using UnityEngine;
using UnityEngine.InputSystem;

public class InputHand : MonoBehaviour
{
    public InputActionProperty ActionValue;
    public InputActionProperty ActionButton;

    void Update()
    {
        float value = ActionValue.action.ReadValue<float>();
        bool button = ActionButton.action.ReadValue<float>() > 0.5f;
    
    }
}
