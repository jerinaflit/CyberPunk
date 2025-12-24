using UnityEngine;

namespace CyberPunk.Core
{
    /// <summary>
    /// A lightweight, "mathematically pure" State Machine.
    /// Optimizes logic by running only the active state's code.
    /// </summary>
    public abstract class StateMachine : MonoBehaviour
    {
        protected State CurrentState;

        protected void ChangeState(State newState)
        {
            if (CurrentState != null)
                CurrentState.Exit();

            CurrentState = newState;
            
            if (CurrentState != null)
            {
                CurrentState.Initialize(this);
                CurrentState.Enter();
            }
        }

        protected virtual void Update()
        {
            if (CurrentState != null)
                CurrentState.Tick();
        }

        protected virtual void FixedUpdate()
        {
            if (CurrentState != null)
                CurrentState.FixedTick();
        }
    }

    public abstract class State
    {
        protected StateMachine Machine;

        public void Initialize(StateMachine machine) => Machine = machine;

        public virtual void Enter() { }
        public virtual void Exit() { }
        public virtual void Tick() { }
        public virtual void FixedTick() { }
    }
}
