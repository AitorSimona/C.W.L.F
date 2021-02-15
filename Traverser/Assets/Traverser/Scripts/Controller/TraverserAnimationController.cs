using UnityEngine;

public class TraverserAnimationController : MonoBehaviour
{
    // --- Attributes ---

    public struct AnimatorParameters
    {
        public bool Move;
        public float Speed;
        public float Heading;

        public int MoveID;
        public int SpeedID;
        public int HeadingID;
    }

    [Header("Animation")]
    [Tooltip("Reference to the skeleton's parent. The controller positions the skeleton at the skeletonRef's position. Used to kill animation's root motion.")]
    public Transform skeleton;
    [Tooltip("Reference to the skeleton's reference position. A transform that follows the controller's object motion, with an offset to the bone position (f.ex hips).")]
    public Transform skeletonRef;


    private Animator animator;
    //private Quaternion initialRotation;

    // --------------------------------

    // --- Basic Methods ---
    private void Start()
    {
        animator = GetComponent<Animator>();
        //initialRotation = skeleton.rotation;
    }

    private void LateUpdate()
    {
        // --- Move all the skeleton to the character's position ---
        skeleton.position = skeletonRef.position;
        skeleton.rotation =  
            transform.rotation * Quaternion.AngleAxis(90, Vector3.up)
            * Quaternion.AngleAxis(skeleton.localRotation.eulerAngles.x, Vector3.right)
            * Quaternion.AngleAxis(skeleton.localRotation.eulerAngles.z, Vector3.forward)
            ;
            
        //skeleton.rotation = transform.rotation * initialRotation;
    }

    // --------------------------------

    // --- Utility Methods ---

    public void InitializeAnimatorParameters(ref AnimatorParameters parameters)
    {
        parameters.Move = false;
        parameters.Speed = 0.0f;
        parameters.Heading = 0.0f;
        parameters.MoveID = Animator.StringToHash("Move");
        parameters.SpeedID = Animator.StringToHash("Speed");
        parameters.HeadingID = Animator.StringToHash("Heading");
    }

    public void UpdateAnimator(ref AnimatorParameters parameters)
    {
        // --- Update animator with the given parameter's values ---
        animator.SetBool(parameters.MoveID, parameters.Move);
        animator.SetFloat(parameters.SpeedID, parameters.Speed);
        animator.SetFloat(parameters.HeadingID, Mathf.Abs(parameters.Heading));

    }

    public bool MatchTarget(Vector3 matchPosition, Quaternion matchRotation, AvatarTarget target, MatchTargetWeightMask weightMask, float normalisedStartTime, float normalisedEndTime)
    {
        // --- Asjusts game object's position and rotation to match givern position and rotation in the given time with current animation ---
        bool ret = false; 

        if (!animator.isMatchingTarget)
        {
            float normalizeTime = Mathf.Repeat(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1.0f);

            if (normalizeTime > normalisedEndTime)
                ret = true;
            else
                animator.MatchTarget(matchPosition, matchRotation, target, weightMask, normalisedStartTime, normalisedEndTime);
        }

        return ret;
    }

    // --------------------------------

}
