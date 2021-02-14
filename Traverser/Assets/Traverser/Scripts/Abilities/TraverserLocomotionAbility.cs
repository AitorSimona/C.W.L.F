﻿using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Assertions;

namespace Traverser
{
    [RequireComponent(typeof(TraverserAbilityController))]
    [RequireComponent(typeof(TraverserCharacterController))]

    public class TraverserLocomotionAbility : MonoBehaviour, TraverserAbility // SnapshotProvider is derived from MonoBehaviour, allows the use of kinematica's snapshot debugger
    {
        // --- Attributes ---
        [Header("Movement settings")]
        [Tooltip("Desired speed in meters per second for slow movement.")]
        [Range(0.0f, 10.0f)]
        public float movementSpeedSlow = 3.9f;

        [Tooltip("Desired speed in meters per second for fast movement.")]
        [Range(0.0f, 10.0f)]
        public float movementSpeedFast = 5.5f;

        [Tooltip("Character won't move if below this speed.")]
        [Range(0.0f, 10.0f)]
        public float movementSpeedMin = 0.1f;

        [Tooltip("How fast the character's speed will increase with given input in m/s^2.")]
        public float movementAcceleration = 50.0f;

        [Tooltip("How fast the character achieves movementAcceleration. Smaller values make the character reach target movementAcceleration faster.")]
        public float movementAccelerationTime = 0.1f;

        [Tooltip("How fast the character decreases movementSpeed. Smaller values make the character reach target movementSpeed slower.")]
        public float movementDecelerationTime = 0.75f;

        [Tooltip("How fast the character reaches its desired movement speed in seconds. Smaller values make character reach movementSpeed slower")]
        public float movementSpeedTime = 0.75f;


        [Header("Rotation settings")]
        [Tooltip("How fast the character rotates in degrees/s.")]
        public float rotationSpeed = 180.0f; // in degrees / second

        [Tooltip("How fast the character's rotation speed will increase with given input in degrees/s^2.")]
        public float rotationAcceleration = 15.0f; // in degrees

        [Tooltip("How fast the character achieves rotationAcceleration. Smaller values make the character reach target rotationAcceleration faster.")]
        public float rotationAccelerationTime = 0.1f;


        [Header("Simulation settings")]
        [Tooltip("How many movement iterations per frame will the controller perform. More iterations are more expensive but provide greater predictive collision detection reach.")]
        [Range(0, 10)]
        public int iterations = 3;
        [Tooltip("How much will the controller displace the 2nd and following iterations respect the first one (speed increase). Increases prediction reach at the cost of precision (void space).")]
        [Range(1.0f, 10.0f)]
        public float stepping = 10.0f;


        //[Tooltip("Speed in meters per second at which the character is considered to be braking (assuming player release the stick).")]
        //[Range(0.0f, 10.0f)]
        //public float brakingSpeed = 0.4f;
        //[Tooltip("How likely are we to deviate from current pose to idle, higher values make faster transitions to idle")]

        // -------------------------------------------------

        // --- Private Variables ---

        private TraverserCharacterController controller;

        // --- Character's target speed for jog/run ---
        private float desiredLinearSpeed => TraverserInputLayer.capture.run ? movementSpeedFast : movementSpeedSlow;
        
        // --- Stores current velocity in m/s ---
        private Vector3 currentVelocity = Vector3.zero;

        // --- Stores current rotation speed in degrees ---
        private float currentRotationSpeed = 0.0f; 

        // --- Stores the current time to reach max movement speed ---
        private float movementAccelerationTimer = 0.0f;

        // --- Sotes movementSpeedTimer's maximum value, has to be 1.0f ---
        private float movementAccelerationMaxTime = 1.0f;
        
        // --- Stores the current time to reach desired velocity (decelerating) ---
        private float movementDecelerationTimer = 1.0f;

        // --- Stores previous inpu intensity to decelerate character ---
        private float previousMovementIntensity = 0.0f;

        // -------------------------------------------------

        // --- Basic Methods ---
        public void OnEnable()
        {
            controller = GetComponent<TraverserCharacterController>();
            TraverserInputLayer.capture.movementDirection = Vector3.zero;
        }

        public void OnDisable()
        {


        }

        // -------------------------------------------------

        // --- Ability class methods ---
        public TraverserAbility OnUpdate(float deltaTime)
        {
            TraverserInputLayer.capture.UpdateLocomotion();

            TraverserAbility ret = this;

            return ret;
        }

        public TraverserAbility OnFixedUpdate(float deltaTime)
        {
            TraverserAbility ret = this;

            // --- We perform future movement to check for collisions, then rewind using snapshot debugger's capabilities ---
            TraverserAbility contactAbility = HandleMovementPrediction(deltaTime);

            // --- Another ability has been triggered ---
            if (contactAbility != null)
                ret = contactAbility;

            return ret;
        }

        public TraverserAbility OnPostUpdate(float deltaTime)
        {

            return null;
        }

        TraverserAbility HandleMovementPrediction(float deltaTime)
        {
            Assert.IsTrue(controller != null); // just in case :)

            // --- Start recording (all current state values will be recorded) ---
            controller.Snapshot();

            TraverserAbility contactAbility = SimulatePrediction(ref controller, deltaTime);

            // --- Go back in time to the snapshot state, all current state variables recover their initial value ---
            controller.Rewind();

            return contactAbility;
        }
        
        TraverserAbility SimulatePrediction(ref TraverserCharacterController controller, float deltaTime)
        {
            bool attemptTransition = true;
            TraverserAbility contactAbility = null;

            // --- Preserve pre-simulation transform ---
            TraverserAffineTransform tmp = TraverserAffineTransform.Create(transform.position, transform.rotation);

            // --- Update current velocity and rotation ---
            UpdateMovement();
            UpdateRotation();

            // --- Compute desired speed ---
            float speed = GetDesiredSpeed(deltaTime);

            // --- Don't move if below minimum speed ---
            if (speed < movementSpeedMin)
                speed = 0.0f;

            // --- Compute desired displacement ---
            Vector3 finalDisplacement = transform.forward * speed * deltaTime;

            // --- Rotate controller ---
            controller.ForceRotate(currentRotationSpeed * deltaTime);

            for (int i = 0; i < iterations; ++i)
            {
                // --- Step simulation if above first tick ---
                if (i == 0)
                    controller.stepping = 1.0f;
                else
                    controller.stepping = stepping;

                // --- Simulate movement ---
                controller.Move(finalDisplacement);
                controller.Tick(deltaTime);

                ref TraverserCharacterController.TraverserCollision collision = ref controller.current; // current state of the controller (after a tick is issued)

                // --- If a collision occurs, call each ability's onContact callback ---

                if (collision.isColliding && attemptTransition)
                {
                    float3 contactPoint = collision.colliderContactPoint;
                    contactPoint.y = controller.position.y;
                    float3 contactNormal = collision.colliderContactNormal;

                    // TODO: Check if works properly
                    float Qangle;
                    Vector3 Qaxis;
                    transform.rotation.ToAngleAxis(out Qangle, out Qaxis);
                    //Qaxis.x = 0.0f;
                    //Qaxis.y = 0.0f;
                    quaternion q = quaternion.identity/*math.mul(transform.rotation, Quaternion.FromToRotation(Qaxis, contactNormal))*/;

                    TraverserAffineTransform contactTransform = TraverserAffineTransform.Create(contactPoint, q);

                    //  TODO : Remove temporal debug object
                    GameObject.Find("dummy").transform.position = contactTransform.t;

                    float3 desired_direction = contactTransform.t - tmp.t;
                    float current_orientation = Mathf.Rad2Deg * Mathf.Atan2(gameObject.transform.forward.z, gameObject.transform.forward.x);
                    float target_orientation = current_orientation + Vector3.SignedAngle(TraverserInputLayer.capture.movementDirection, desired_direction, Vector3.up);
                    float angle = -Mathf.DeltaAngle(current_orientation, target_orientation);

                    // TODO: The angle should be computed according to the direction we are heading too (not always the smallest angle!!)
                    //Debug.Log(angle);
                    // --- If we are not close to the desired angle or contact point, do not handle contacts ---
                    if (Mathf.Abs(angle) < 30 || Mathf.Abs(math.distance(contactTransform.t, tmp.t)) > 4.0f)
                    {
                        continue;
                    }

                    if (contactAbility == null)
                    {
                        foreach (TraverserAbility ability in GetComponents(typeof(TraverserAbility)))
                        {
                            // --- If any ability reacts to the collision, break ---
                            if (ability.IsAbilityEnabled() && ability.OnContact(contactTransform, deltaTime))
                            {
                                contactAbility = ability;
                                break;
                            }
                        }
                    }

                    attemptTransition = false; // make sure we do not react to another collision
                }
                else if (!controller.isGrounded) // we are dropping/falling down
                {
                    // --- Let other abilities take control on drop ---
                    if (contactAbility == null)
                    {
                        foreach (TraverserAbility ability in GetComponents(typeof(TraverserAbility)))
                        {
                            // --- If any ability reacts to the drop, break ---
                            if (ability.IsAbilityEnabled() && ability.OnDrop(deltaTime))
                            {
                                contactAbility = ability;
                                break;
                            }
                        }
                    }
                }
            }


            ResetVelocityAndRotation();

            return contactAbility;
        }

        public bool OnContact(TraverserAffineTransform contactTransform, float deltaTime)
        {
            return false;
        }

        public bool OnDrop(float deltaTime)
        {
            return false;
        }

        public void OnAbilityAnimatorMove() // called by ability controller at OnAnimatorMove()
        {

        }

        public bool IsAbilityEnabled()
        {
            return isActiveAndEnabled;
        }

        // -------------------------------------------------

        // --- Movement ---

        float GetDesiredSpeed(float deltaTime)        
        {
            float desiredSpeed = 0.0f;
            float moveIntensity = GetDesiredMovementIntensity(deltaTime);

            // --- Increase timer ---
            movementAccelerationTimer += movementSpeedTime*deltaTime;

            // --- Cap timer ---
            if (movementAccelerationTimer > movementAccelerationMaxTime)
                movementAccelerationTimer = movementAccelerationMaxTime;

            // --- Compute desired speed given input intensity and timer ---
            desiredSpeed = desiredLinearSpeed * movementAccelerationTimer * moveIntensity;

            // --- Reset/Decrease timer if input intensity changes ---
            if (desiredSpeed == 0.0f)
                movementAccelerationTimer = 0.0f;
            else if (moveIntensity < movementAccelerationTimer)
                movementAccelerationTimer = moveIntensity;

            return desiredSpeed;
        }

        float GetDesiredMovementIntensity(float deltaTime)
        {
            // --- Compute desired movement intensity given input and timer ---
            float moveIntensity = TraverserInputLayer.GetMoveIntensity();

            // --- Cap timer, reset previous movment intensity ---
            if (movementDecelerationTimer < 0.0f)
            {
                movementDecelerationTimer = 0.0f;
                previousMovementIntensity = 0.0f;
            }

            // --- Decrease timer if asked to decelerate, update previous movement intensity ---
            if (moveIntensity < previousMovementIntensity)
            {
                moveIntensity = movementDecelerationTimer;
                previousMovementIntensity = moveIntensity + 0.01f; 
                movementDecelerationTimer -= movementDecelerationTime * deltaTime;
            }
            // --- Update timer if accelerating/constant acceleration ---
            else
            {
                movementDecelerationTimer = moveIntensity;
                previousMovementIntensity = moveIntensity;
            }

            return moveIntensity;
        }

        public void AccelerateMovement(Vector3 acceleration)
        {
            if (acceleration.magnitude > movementAcceleration)
                acceleration = acceleration.normalized * movementAcceleration;

            currentVelocity += acceleration;
        }

        public void AccelerateRotation(float rotation_acceleration)
        {
            Mathf.Clamp(rotation_acceleration, -rotationAcceleration, rotationAcceleration);
            currentRotationSpeed += rotation_acceleration;
        }

        public void ResetVelocityAndRotation()
        {
            currentVelocity = Vector3.zero;
            currentRotationSpeed = 0.0f;
        }

        public void UpdateMovement()
        {
            // --- Gather current input in Vector3 format ---
            Vector3 inputDirection;
            inputDirection.x = TraverserInputLayer.capture.stickHorizontal;
            inputDirection.y = 0.0f;
            inputDirection.z = TraverserInputLayer.capture.stickVertical;

            // --- Compute desired acceleration given input, current velocity, and time to accelerate ---
            Vector3 acceleration = (inputDirection*desiredLinearSpeed - currentVelocity) / movementAccelerationTime;

            // --- Cap acceleration ---
            if (acceleration.magnitude > movementAcceleration)
            {
                acceleration.Normalize();
                acceleration *= movementAcceleration;
            }

            // --- Update velocity ---
            AccelerateMovement(acceleration);

            // --- Cap Velocity ---
            currentVelocity.x = Mathf.Clamp(currentVelocity.x, -desiredLinearSpeed, desiredLinearSpeed);
            currentVelocity.z = Mathf.Clamp(currentVelocity.z, -desiredLinearSpeed, desiredLinearSpeed);
            currentVelocity.y = Mathf.Clamp(currentVelocity.y, -desiredLinearSpeed, desiredLinearSpeed);
        }

        public void UpdateRotation()
        {
            // --- Compute desired yaw rotation from forward to currentVelocity ---
            float rot = Vector3.SignedAngle(transform.forward, currentVelocity, Vector3.up);

            if (rot > rotationSpeed)
                rot = rotationSpeed;

            // --- Update rotation speed ---
            AccelerateRotation(rot / rotationAccelerationTime);

            // --- Cap Rotation ---
            currentRotationSpeed = Mathf.Clamp(currentRotationSpeed, -rotationSpeed, rotationSpeed);
        }

        // -------------------------------------------------
    }

    // -------------------------------------------------
}
