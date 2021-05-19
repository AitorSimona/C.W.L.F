using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Traverser
{
    public partial class TraverserCharacterController : MonoBehaviour
    {
        // --- Wrapper to define a controller's state collision situation and movement information ---
        public struct TraverserCollision
        {
            // --- Attributes ---

            // --- The collider we just made contact with ---
            public Collider collider;

            // --- States if we are currently colliding ---
            public bool isColliding;

            // --- The point at which we collided with the current collider ---
            public float3 colliderContactPoint;

            // --- The current collider's normal direction ---
            public float3 colliderContactNormal;

            // --- Transform of the current ground, the object below the character ---
            public Collider ground;

            // --- States if the controller is grounded ---
            public bool isGrounded;

            // --- The controller's position (simulation) ---
            public float3 position;

            // --- The controller's velocity (simulation) ---
            public float3 velocity;

            // --- The controller's current kinematic based displacement ---
            public float3 kinematicDisplacement;

            // --- The controller's current dynamics based displacement ---
            public float3 dynamicsDisplacement;

            // --------------------------------

            // --- Basic methods ---

            internal static TraverserCollision Create()
            {
                return new TraverserCollision()
                {
                    position = float3.zero,
                    velocity = float3.zero,
                    collider = null,
                    isColliding = false,
                    colliderContactNormal = float3.zero,
                    colliderContactPoint = float3.zero,
                    ground = null,
                    isGrounded = false,
                    kinematicDisplacement = Vector3.zero,
                    dynamicsDisplacement = Vector3.zero,
                };
            }

            internal void Reset()
            {
                position = float3.zero;
                velocity = float3.zero;
                collider = null;
                isColliding = false;
                colliderContactNormal = float3.zero;
                colliderContactPoint = float3.zero;
                ground = null;
                isGrounded = false;
                kinematicDisplacement = float3.zero;
                dynamicsDisplacement = float3.zero;
            }

            internal void CopyFrom(ref TraverserCollision copyCollision)
            {
                collider = copyCollision.collider;
                isColliding = copyCollision.isColliding;
                colliderContactPoint = copyCollision.colliderContactPoint;
                colliderContactNormal = copyCollision.colliderContactNormal;
                ground = copyCollision.ground;
                position = copyCollision.position;
                velocity = copyCollision.velocity;
                isGrounded = copyCollision.isGrounded;
                kinematicDisplacement = copyCollision.kinematicDisplacement;
                dynamicsDisplacement = copyCollision.dynamicsDisplacement;
            }

            // --------------------------------
        }

        // --- Wrapper to define a controller's state, including previous and current collision situations ---
        public struct TraverserState
        {
            // --- Attributes ---

            // --- The state's previous collision situation --- 
            public TraverserCollision previousCollision;

            // --- The state's actual collision situation ---
            public TraverserCollision currentCollision;

            // --- The state's desired absolute displacement ---
            public float3 desiredDisplacement;

            // --------------------------------

            // --- Basic methods ---

            internal static TraverserState Create()
            {
                return new TraverserState()
                {
                    previousCollision = TraverserCollision.Create(),
                    currentCollision = TraverserCollision.Create(),
                    desiredDisplacement = float3.zero,
                };
            }

            internal void CopyFrom(ref TraverserState copyState)
            {
                previousCollision.CopyFrom(ref copyState.previousCollision);
                currentCollision.CopyFrom(ref copyState.currentCollision);
                desiredDisplacement = copyState.desiredDisplacement;
            }

            // --------------------------------
        }

        // --------------------------------

        // --- Private Variables ---

        // --- Unity's character controller ---
        private CharacterController characterController;

        // --- Actual controller state ---
        private TraverserState state;

        // --- State snapshot to save current state before movement simulation ---
        private TraverserState snapshotState;

        // --- The last contact position and rotation extracted from last collision ---
        private TraverserTransform lastContactTransform = TraverserTransform.Get(float3.zero, quaternion.identity);

        // --- Array of colliders for ground probing ---
        private Collider[] hitColliders;

        // --- Ray for groundSnap checks ---
        private Ray groundRay;

        // --- Arrays of positions for geometry debugging ---
        private List<float3> probePositions;
        private List<float3> capsulePositions;
        private List<float3> planePositions;

        // --- Simulation counter (keep track of current iteration) ---
        private int simulationCounter;

        // --- Keeps track of current ground snap value ---
        private bool currentGroundSnap;

        // --- Keeps track of current gravity value ---
        private bool currentGravity;

        // --------------------------------

        // --- Basic methods ---

        // Start is called before the first frame update
        void Start()
        {
            // --- Initialize controller ---
            characterController = GetComponent<CharacterController>();
            state = TraverserState.Create();
            snapshotState = TraverserState.Create();
            lastContactTransform = TraverserTransform.Get(float3.zero, quaternion.identity);
            hitColliders = new Collider[3];
            groundRay = new Ray();

            state.currentCollision.position = transform.position;
            targetPosition = transform.position;
            targetVelocity = float3.zero;
            targetHeading = 0.0f;

            // --- Initialize debug lists (consider commenting in build, with debugDraw set to false) ---
            probePositions = new List<float3>(3);
            planePositions = new List<float3>(3);
            capsulePositions = new List<float3>(3);

            simulationCounter = 0;
            currentGroundSnap = groundSnap;
            currentGravity = gravityEnabled;
        }

        // --------------------------------

        // --- Collisions ---

        void CheckGroundCollision()
        {
            int colliderIndex = TraverserCollisionLayer.CastGroundProbe(position, groundProbeRadius, ref hitColliders, TraverserCollisionLayer.EnvironmentCollisionMask);

            if (colliderIndex != -1)
            {   
                // --- Set the closest collider as our ground ---  
                current.ground = hitColliders[colliderIndex];
                current.isGrounded = true;
            }      

            // --- Add cast position to debug draw lists ---
            if (debugDraw)
            {
                if (probePositions.Count == simulationCounter)
                    probePositions.Add(position);
                else
                    probePositions[simulationCounter] = position;

                if (planePositions.Count == simulationCounter)
                    planePositions.Add(characterController.bounds.min + Vector3.forward * characterController.radius + Vector3.right * characterController.radius + Vector3.up * groundProbeRadius);
                else
                    planePositions[simulationCounter] = characterController.bounds.min + Vector3.forward * characterController.radius + Vector3.right * characterController.radius + Vector3.up * groundProbeRadius;
            }

            // --- Prevent drop/fall, snap to ground ---

            if (currentGroundSnap)
            {
                groundRay.origin = characterController.transform.position;
                groundRay.direction = -Vector3.up;

                if (state.previousCollision.ground != null 
                    && !Physics.Raycast(groundRay.origin, groundRay.direction, groundSnapRayDistance, TraverserCollisionLayer.EnvironmentCollisionMask, QueryTriggerInteraction.Ignore))
                {
                    // --- We want to slide along the edge of the collider, not get trapped on it, so we need to properly adjust the trajectory ---

                    // --- Obtain collider's closest point and compute a new velocity vector ---
                    float3 correctedPosition = state.previousCollision.position;
                    float3 closestColliderPosition = state.previousCollision.ground.ClosestPoint(state.currentCollision.position);
                    Vector3 correctedVelocityVector = closestColliderPosition - state.previousCollision.position;

                    // --- Project our current velocity on the newly computed velocity vector ---                   
                    float3 desiredVelocity = math.projectsafe(state.currentCollision.velocity, correctedVelocityVector);
                    correctedPosition += desiredVelocity * Time.deltaTime;
                    state.currentCollision.velocity = desiredVelocity / stepping;

                    // --- Manually correct controller's position ---
                    characterController.enabled = false;
                    transform.position = correctedPosition;
                    characterController.enabled = true;
                    state.currentCollision.position = transform.position;                   
                }

                // --- Draw casted ray ---
                if (debugDraw)
                    Debug.DrawRay(groundRay.origin, groundRay.direction * groundSnapRayDistance);
            }
        }

        // --------------------------------

        // --- Events ---

        void OnDrawGizmosSelected()
        {
            if (!debugDraw || characterController == null)
                return;

            // --- Draw last contact point ---
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(contactTransform.t, contactDebugSphereRadius);


            // --- Draw sphere at current ground probe position ---
            for (int i = 0; i < probePositions.Count; ++i)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(probePositions[i], groundProbeRadius);
            }
         
            // --- Draw ground collision height limit (if below limit this ground won't be detected in regular collisions) ---
            float3 planeScale = Vector3.one;
            planeScale.y = 0.05f;

            Gizmos.color = Color.yellow;

            for (int i = 0; i < planePositions.Count; ++i)
            {
                Gizmos.DrawWireCube(planePositions[i], Vector3.one * planeScale);
            }

            // --- Draw capsule at last simulation position ---      
            if (capsuleDebugMesh != null)
            {
                Gizmos.color = Color.red;

                for (int i = 0; i < capsulePositions.Count; ++i)
                {
                    Gizmos.DrawWireMesh(capsuleDebugMesh, 0, capsulePositions[i], Quaternion.identity, capsuleDebugMeshScale);
                }
            }
            
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // --- Update current collision information with hit's information ---

            // --- If below ground probe limit, consider it only as ground, not regular collision ---
            float heightLimit = characterController.bounds.min.y + groundProbeRadius;

            if (hit.point.y > heightLimit) // if the slope is steep we may activate a collision against current ground
            {
                // --- Make sure we do not activate a collision against our current ground ---
                if (state.previousCollision.ground != null && hit.collider.Equals(state.previousCollision.ground))
                    return;
              
                // --- Given hit normal, compute what is the relevant collider size to store ---

                // --- Convert collided object's axis to character space ---
                Vector3 right = transform.InverseTransformDirection(hit.transform.right);
                Vector3 forward = transform.InverseTransformDirection(hit.transform.forward);

                // --- If you wanted to do the above manually ---
                //Matrix4x4 m = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);      
                //Vector3 f = m.inverse.MultiplyVector(hit.transform.forward);

                float angle = Vector3.SignedAngle(hit.normal, right, Vector3.up);
                float angle2 = Vector3.SignedAngle(hit.normal, forward, Vector3.up);

                if (Mathf.Abs(angle2) < Mathf.Abs(angle))
                    contactSize = hit.collider.bounds.size.z;
                else
                    contactSize = hit.collider.bounds.size.x;

                // --- Retrieve collider data ---
                contactNormal = hit.normal;
                contactTransform.t = hit.point;
                contactTransform.q = math.mul(transform.rotation, Quaternion.FromToRotation(-transform.forward, hit.normal));
                state.currentCollision.colliderContactPoint = hit.point;
                state.currentCollision.colliderContactNormal = hit.normal;
                state.currentCollision.collider = hit.collider;
                state.currentCollision.isColliding = true;
            }
        }

        // --------------------------------
    }
}