using System;
using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared.Leap
{
    [RegisterComponent, NetworkedComponent]
    public sealed partial class LeapComponent : Component
    {
        // Sidebar action string
        [DataField("action", customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
        public string Action = "ActionLeapForward";

        // Actual action
        [DataField("actionEntity")] public EntityUid? ActionEntity;

        // How long the leap lasts
        [DataField("duration")]
        public float Duration = 1f;

        // Speed entity is set to during leap
        [DataField("speed")]
        public float Speed = 5f;

        // Whether this entity should be able to leap in no gravity environments
        [DataField("requiresGravity")]
        public bool RequiresGravity = false; //Magboots do count as having gravity

        // Whether this entity should be able to leap if there is no surface nearby, used if the gravity requirement is false
        [DataField("requiresGrounded")]
        public bool RequiresGrounded = true;

        // The stamina that will be used up in a leap
        [DataField("staminaCost")]
        public int StaminaCost = 20;

        [ViewVariables]
        public Dictionary<string, int> DisabledFixtureMasks { get; } = new();

        [ViewVariables]
        public bool Jumping = false;

        [ViewVariables]
        public bool CheckColliding = false;

        public sealed partial class LeapForwardEvent : InstantActionEvent { }
    }
}
