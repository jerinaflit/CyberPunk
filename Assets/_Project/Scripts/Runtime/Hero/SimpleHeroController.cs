using UnityEngine;
using CyberPunk.Core;
using UnityEngine.InputSystem;

namespace CyberPunk.Hero
{
    /// <summary>
    /// Advanced Hero Controller using Finite State Machine (FSM).
    /// Separates logic into discrete states (Idle, Move) for cleaner, bug-free code.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class SimpleHeroController : StateMachine
    {
        [Header("Physics Settings")]
        public float moveSpeed = 5f;
        public float acceleration = 20f; // High value = snappy, Low = slippery
        public float deceleration = 15f;

        [Header("References")]
        public Animator animator;
        public SpriteRenderer spriteRenderer;

        [Header("Animation")]
        [Tooltip("Seconds of staying idle before playing Idle2 (Idle1 -> Idle2 -> Idle1).")]
        public float idle2Delay = 8f;
        public float idle2Duration = 1.25f;

        // Public properties for States to access
        public Vector2 InputVector { get; private set; }
        public Vector2 TargetPosition { get; private set; } // For Click-to-Move
        public Rigidbody2D Rb { get; private set; }
        public Vector2 CurrentVelocity { get; set; } // For manual physics calc

        private int _facing = 1;
        private float _idleTimer;
        private float _idle2Until;

        // States
        public IdleState StateIdle { get; private set; }
        public MoveState StateMove { get; private set; }
        public WalkToState StateWalkTo { get; private set; }

        private void Awake()
        {
            Rb = GetComponent<Rigidbody2D>();
            Rb.gravityScale = 0;
            Rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            Rb.interpolation = RigidbodyInterpolation2D.Interpolate; // SMOOTH PHYSICS

            if (animator == null) animator = GetComponent<Animator>();
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();

            _facing = 1;
            _idleTimer = 0f;
            _idle2Until = -1f;

            // Initialize States
            StateIdle = new IdleState(this);
            StateMove = new MoveState(this);
            StateWalkTo = new WalkToState(this);
        }

        private void Start()
        {
            ChangeState(StateIdle);
        }

        // Input System Callback
        public void OnMove(InputValue value)
        {
            InputVector = value.Get<Vector2>();
        }

        // Public API for Click-to-Move
        public void MoveTo(Vector2 target)
        {
            TargetPosition = target;
            ChangeState(StateWalkTo);
        }

        private void SetFacing(float desiredX)
        {
            if (Mathf.Abs(desiredX) < 0.001f) return;

            int desiredFacing = desiredX < 0 ? -1 : 1;
            if (desiredFacing != _facing)
            {
                _facing = desiredFacing;
                if (animator != null) animator.SetTrigger("Turn");
            }

            if (spriteRenderer != null)
                spriteRenderer.flipX = _facing < 0;
        }

        private void TickIdle2()
        {
            // Only meaningful if the Animator has an "Idle2" bool.
            if (animator == null) return;

            // Sequence: Idle1 for idle2Delay seconds -> Idle2 for idle2Duration -> back to Idle1.
            if (_idle2Until > 0f)
            {
                if (Time.time >= _idle2Until)
                {
                    animator.SetBool("Idle2", false);
                    _idle2Until = -1f;
                    _idleTimer = 0f;
                }
                return;
            }

            _idleTimer += Time.deltaTime;

            if (_idleTimer >= Mathf.Max(0f, idle2Delay))
            {
                animator.SetBool("Idle2", true);
                _idle2Until = Time.time + Mathf.Max(0f, idle2Duration);
            }
        }

        // --- STATES DEFINITIONS ---

        public class IdleState : State
        {
            private SimpleHeroController _hero;
            public IdleState(SimpleHeroController hero) => _hero = hero;

            public override void Enter()
            {
                if (_hero.animator != null)
                {
                    _hero.animator.SetBool("IsMoving", false);
                    _hero.animator.SetBool("Idle2", false);
                }
                
                if (_hero.Rb != null)
                    _hero.Rb.linearVelocity = Vector2.zero; 

                _hero._idleTimer = 0f;
                _hero._idle2Until = -1f;
            }

            public override void Tick()
            {
                _hero.TickIdle2();

                // Transition Condition: Input detected
                if (_hero.InputVector.sqrMagnitude > 0.01f)
                {
                    _hero.ChangeState(_hero.StateMove);
                }
            }
        }

        public class MoveState : State
        {
            private SimpleHeroController _hero;

            public MoveState(SimpleHeroController hero) => _hero = hero;

            public override void Enter()
            {
                if (_hero.animator != null)
                    _hero.animator.SetBool("IsMoving", true);
            }

            public override void Tick()
            {
                // Transition Condition: No input
                if (_hero.InputVector.sqrMagnitude < 0.01f)
                {
                    _hero.ChangeState(_hero.StateIdle);
                    return;
                }

                // Visuals
                _hero.SetFacing(_hero.InputVector.x);
            }

            public override void FixedTick()
            {
                Vector2 targetVel = _hero.InputVector * _hero.moveSpeed;
                Vector2 newVel = Vector2.MoveTowards(_hero.Rb.linearVelocity, targetVel, _hero.acceleration * Time.fixedDeltaTime);
                _hero.Rb.linearVelocity = newVel;
            }
        }

        public class WalkToState : State
        {
            private SimpleHeroController _hero;
            private const float StopDistance = 0.1f;

            public WalkToState(SimpleHeroController hero) => _hero = hero;

            public override void Enter()
            {
                if (_hero.animator != null)
                    _hero.animator.SetBool("IsMoving", true);
            }

            public override void Tick()
            {
                // Override if player touches keys
                if (_hero.InputVector.sqrMagnitude > 0.01f)
                {
                    _hero.ChangeState(_hero.StateMove);
                    return;
                }

                // Check distance
                float dist = Vector2.Distance(_hero.transform.position, _hero.TargetPosition);
                if (dist < StopDistance)
                {
                    _hero.ChangeState(_hero.StateIdle);
                    return;
                }

                // Visuals (Flip)
                float dirX = _hero.TargetPosition.x - _hero.transform.position.x;
                _hero.SetFacing(dirX);
            }

            public override void FixedTick()
            {
                // Move towards target
                Vector2 currentPos = _hero.Rb.position;
                Vector2 direction = (_hero.TargetPosition - currentPos).normalized;
                
                Vector2 targetVel = direction * _hero.moveSpeed;
                Vector2 newVel = Vector2.MoveTowards(_hero.Rb.linearVelocity, targetVel, _hero.acceleration * Time.fixedDeltaTime);
                
                _hero.Rb.linearVelocity = newVel;
            }
        }
    }
}
