using Content.Shared.DoAfter;
using Content.Shared.Popups;
using Content.Shared.Actions;
using Content.Shared.Actions.ActionTypes;
using Robust.Shared.Prototypes;
using Content.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Content.Shared.Leap;
using Content.Shared.Climbing;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Dynamics;
using Content.Shared.GameTicking;
using System.Numerics;
using Content.Shared.Damage.Systems;
using Content.Shared.Damage.Components;
using Content.Shared.IdentityManagement;
using static Content.Shared.Leap.LeapComponent;

namespace Content.Shared.Leap;

public sealed class LeapSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly FixtureSystem _fixtureSystem = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly StaminaSystem _staminaSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;

    private const int LeapingCollisionGroup = (int) (CollisionGroup.TableLayer | CollisionGroup.LowImpassable);
    private const string LeapingFixtureName = "Leaping";

    private readonly Dictionary<EntityUid, List<Fixture>> _fixtureRemoveQueue = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(Reset);

        SubscribeLocalEvent<LeapComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<LeapComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<LeapComponent, LeapForwardEvent>(HandleLeap);
        SubscribeLocalEvent<LeapComponent, LeapFinishEvent>(FinishLeap);
        SubscribeLocalEvent<LeapComponent, EndCollideEvent>(OnLeapCollisionEnd);
    }

    private void OnComponentInit(EntityUid uid, LeapComponent component, ComponentInit args)
    {
        component.StoredAction = new(_prototype.Index<InstantActionPrototype>(component.LeapForwardAction));

        _actions.AddAction(uid, component.StoredAction, null);
    }

    private void OnComponentShutdown(EntityUid uid, LeapComponent component, ComponentShutdown args)
    {
        if (component.StoredAction != null)
            _actions.RemoveAction(uid, component.StoredAction);
    }

    private void HandleLeap(EntityUid uid, LeapComponent component, LeapForwardEvent args)
    {
        if (args.Handled)
            return;

        Log.Debug("Preparing Leap");
        var success = AttemptLeap(uid, component);
        Log.Debug("Leap Handled");

        args.Handled = success;
    }

    private bool AttemptLeap(EntityUid uid,
    LeapComponent? leapComp = null,
    PhysicsComponent? physicsComp = null,
    FixturesComponent? fixtureComp = null,
    ClimbingComponent? climbingComp = null)
    {

        if (!Resolve(uid, ref fixtureComp, ref physicsComp, ref leapComp))
            return false;

        if (leapComp.Jumping == true)
            return false;

        //if (Resolve(uid, ref climbingComp))
        //{
        //    if ( climbingComp.IsClimbing == true )
        //        return false;
        //    climbingComp.IsClimbing = true;
        //}

        if (!_staminaSystem.TryTakeStamina(uid, leapComp.StaminaCost))
        {
            Log.Debug("Not enough Stamina");
            return false;
        }

        foreach (var (name, fixture) in fixtureComp.Fixtures)
        {
            if (leapComp.DisabledFixtureMasks.ContainsKey(name)
                || fixture.Hard == false
                || (fixture.CollisionMask & LeapingCollisionGroup) == 0)
                continue;

            leapComp.DisabledFixtureMasks.Add(fixture.ID, fixture.CollisionMask & LeapingCollisionGroup);
            _physics.SetCollisionMask(uid, fixture, fixture.CollisionMask & ~LeapingCollisionGroup, fixtureComp);
        }

        if (!fixtureComp.Fixtures.ContainsKey(LeapingFixtureName)){
            if (!_fixtureSystem.TryCreateFixture(
                    uid,
                    new PhysShapeCircle(0.35f),
                    LeapingFixtureName,
                    collisionLayer: (int) CollisionGroup.None,
                    collisionMask: LeapingCollisionGroup,
                    hard: false,
                    manager: fixtureComp))
            {
                return false;
            }
        }

        _physics.SetBodyStatus(physicsComp, BodyStatus.InAir);
        var ang = _transform.GetWorldRotation(uid);
        _physics.SetLinearVelocity(uid, ang.RotateVec(new Vector2(0, -leapComp.Speed)));

        var message = Loc.GetString("comp-leap-user-leaps-other", ("user", Identity.Entity(uid, EntityManager)));

        _popupSystem.PopupEntity(message, uid, PopupType.Medium);

        leapComp.Jumping = true;
        leapComp.CheckColliding = false;

        var doAfterArgs = new DoAfterArgs(uid, leapComp.Duration, new LeapFinishEvent(), uid)
        {
            BreakOnUserMove = false,
            BlockDuplicate = true,
            BreakOnDamage = false,
            CancelDuplicate = true,
        };

        _doAfterSystem.TryStartDoAfter(doAfterArgs);

        return true;
    }

    private void FinishLeap(EntityUid uid, LeapComponent leapComp, LeapFinishEvent args)
    {
        PhysicsComponent? physics = null;
        if (!Resolve(uid, ref physics))
            return;

        leapComp.CheckColliding = true;
        leapComp.Jumping = false;
        _physics.SetBodyStatus(physics, BodyStatus.OnGround);

        FinishCheck(uid, leapComp);
    }

    private void FinishCheck(EntityUid uid, LeapComponent leapComp, PhysicsComponent? physComp = null)
    {
        FixturesComponent? fixtureComp = null;
        if (!Resolve(uid, ref fixtureComp, ref physComp))
            return;

        var contactingEntities = _physics.GetEntitiesIntersectingBody(uid, 30);

        foreach (var entities in contactingEntities)
        {
            //if (fixture == args.OtherFixture)
            //continue;
            if (HasComp<ClimbableComponent>(entities))
                return;
        }

        ReturnLeapCollisions(uid, leapComp);
    }

    private void OnLeapCollisionEnd(EntityUid uid, LeapComponent leapComp, ref EndCollideEvent args)
    {
        if (args.OurFixture.ID != LeapingFixtureName
            || leapComp.Jumping
            || !leapComp.CheckColliding)
            return;

        Log.Debug("Exited Contact");

        foreach (var fixture in args.OurFixture.Contacts.Keys)
        {
            if (fixture == args.OtherFixture)
                continue;
            if (HasComp<ClimbableComponent>(args.OtherEntity))
                return;
        }

        Log.Debug("Confirmed Clear");

        leapComp.Jumping = false;
        leapComp.CheckColliding = false;

        //ClimbingComponent? climbingComp = null;

        //if(Resolve(uid, ref climbingComp))
            //climbingComp.IsClimbing = false;

        ReturnLeapCollisions(uid, leapComp);
    }

    private void ReturnLeapCollisions(EntityUid uid, LeapComponent? leapComp = null, FixturesComponent? fixtures = null, ClimbingComponent? climbComp = null)
    {
        if (!Resolve(uid, ref fixtures, ref leapComp))
            return;

        foreach (var (name, fixtureMask) in leapComp.DisabledFixtureMasks)
        {
            if (!fixtures.Fixtures.TryGetValue(name, out var fixture))
            {
                continue;
            }

            _physics.SetCollisionMask(uid, fixture, fixture.CollisionMask | fixtureMask, fixtures);
        }
        leapComp.DisabledFixtureMasks.Clear();

        if (!_fixtureRemoveQueue.TryGetValue(uid, out var removeQueue))
        {
            removeQueue = new List<Fixture>();
            _fixtureRemoveQueue.Add(uid, removeQueue);
        }

        if (fixtures.Fixtures.TryGetValue(LeapingFixtureName, out var leapingFixture))
            removeQueue.Add(leapingFixture);

        //if (Resolve(uid, ref climbComp))
        //    climbComp.IsClimbing = false;
    }

    public override void Update(float frameTime)
    {
        foreach (var (uid, fixtures) in _fixtureRemoveQueue)
        {
            if (!TryComp<PhysicsComponent>(uid, out var physicsComp)
                || !TryComp<FixturesComponent>(uid, out var fixturesComp))
            {
                continue;
            }

            foreach (var fixture in fixtures)
            {
                _fixtureSystem.DestroyFixture(uid, fixture, body: physicsComp, manager: fixturesComp);
            }
        }

        _fixtureRemoveQueue.Clear();
    }

    private void Reset(RoundRestartCleanupEvent ev)
    {
        _fixtureRemoveQueue.Clear();
    }
}
