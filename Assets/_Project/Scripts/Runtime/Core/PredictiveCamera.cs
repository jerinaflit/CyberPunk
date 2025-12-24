using UnityEngine;

namespace CyberPunk.Core
{
    /// <summary>
    /// A "Predictive" Camera that looks ahead of the player.
    /// Uses mathematical damping to smooth out jitter.
    /// </summary>
    public class PredictiveCamera : MonoBehaviour
    {
        [Header("Targeting")]
        public Transform target;
        public Vector3 offset = new Vector3(0, 0, -10);

        [Header("Math Settings")]
        [Tooltip("How far ahead to look based on target velocity.")]
        public float lookAheadFactor = 1.5f;
        [Tooltip("Clamp for the look-ahead distance (prevents camera going crazy on fast acceleration).")]
        public float maxLookAheadDistance = 2.5f;
        [Tooltip("Smoothing time. Lower = faster catchup.")]
        public float smoothTime = 0.25f;
        [Tooltip("Limits the maximum speed of the camera.")]
        public float maxSpeed = 20f;
        [Tooltip("Limits camera movement per frame to prevent sudden jumps.")]
        public float maxStepPerFrame = 0.75f;

        private Vector3 _currentVelocity;
        private Rigidbody2D _targetRb;

        private void Start()
        {
            if (target != null)
                _targetRb = target.GetComponent<Rigidbody2D>();
        }

        private void LateUpdate()
        {
            if (target == null) return;

            // 1. Base Position
            Vector3 targetPos = target.position + offset;

            // 2. Predictive Look-Ahead (Math Magic)
            if (_targetRb != null)
            {
                // We project the camera position forward based on the player's velocity
                Vector3 lookAhead = (Vector3)_targetRb.linearVelocity * lookAheadFactor;
                if (maxLookAheadDistance > 0f)
                    lookAhead = Vector3.ClampMagnitude(lookAhead, maxLookAheadDistance);
                targetPos += lookAhead;
            }

            // 3. Smooth Damping (Critical for "Pro" feel)
            Vector3 smoothed = Vector3.SmoothDamp(transform.position, targetPos, ref _currentVelocity, smoothTime, maxSpeed);

            if (maxStepPerFrame > 0f)
            {
                Vector3 delta = smoothed - transform.position;
                float maxStep = maxStepPerFrame;
                if (delta.magnitude > maxStep)
                    smoothed = transform.position + delta.normalized * maxStep;
            }

            transform.position = smoothed;
        }
    }
}
