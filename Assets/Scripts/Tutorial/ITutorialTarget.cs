using System;

namespace VRSwat.Tutorial
{
    // Bia của Việt (StaticTarget, PopUpTarget, MovingTarget) implement interface này.
    public interface ITutorialTarget
    {
        event Action<ITutorialTarget> Knocked;   // gọi khi bia bị bắn hạ
        bool IsCivilian { get; }
    }
}