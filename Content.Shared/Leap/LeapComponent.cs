using System;
using Content.Shared.Actions;
using Content.Shared.Actions.ActionTypes;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared.Leap
{
    [RegisterComponent, NetworkedComponent]
    public sealed class LeapComponent : Component
    {
        // Sidebar action string
        [DataField("leapForwardAction", customTypeSerializer: typeof(PrototypeIdSerializer<InstantActionPrototype>))]
        public string LeapForwardAction = "LeapForward";

        // Actual action
        [DataField("storedAction")]
        public InstantAction? StoredAction;

        // How long the leap lasts
        [DataField("duration")]
        public float Duration = 1f;

        [DataField("speed")]
        public float Speed = 5f;

        [DataField("staminaCost")]
        public int StaminaCost = 20;

        [ViewVariables]
        public Dictionary<string, int> DisabledFixtureMasks { get; } = new();

        [ViewVariables]
        public bool Jumping = false;

        [ViewVariables]
        public bool CheckColliding = false;

        [ViewVariables]
        public TimeSpan CompleteTime = TimeSpan.FromSeconds(0);

        [Serializable, NetSerializable]
        public sealed class LeapForwardEvent : InstantActionEvent { }
    }
}
