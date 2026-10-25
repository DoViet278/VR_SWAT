using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FireRangeTarget : BaseHittableObject
{
    public float MoveSpeed = 10f;
    public float Acceleration = 10f;
    public bool DefaultIsFar = false;
    public float MinPosition = 1;
    public float MaxPosition = 10;
    public Animator TargetAnimator;
    public Transform TargetTransform;
    public AudioSource LoopSound;
    public float MinPitch = 0;
    public float MaxPitch = 1;
    public float MaxVolume = 1;
    public AnimationCurve LoopCurve;


    [SerializeField]
    bool currentIsFar = false;
    [SerializeField]
    bool isMoving = false;

    float speed = 0;
    float maxDistance;

    Vector3 movementMask = new Vector3(1f, 0f, 0f);
    
	void Start ()
    {
        currentIsFar = DefaultIsFar;

        gameObject.SetActive(false);
        if (currentIsFar)
        {
            Vector3 pos = TargetTransform.localPosition;
            for (int i = 0; i < 3; i++)
            {
                if (movementMask[i] == 0)
                    continue;

                pos[i] = MaxPosition;
            }
            TargetTransform.localPosition = pos;
        }
        else
        {
            Vector3 pos = TargetTransform.localPosition;
            for (int i = 0; i < 3; i++)
            {
                if (movementMask[i] == 0)
                    continue;

                pos[i] = MinPosition;
            }
            TargetTransform.localPosition = pos;
        }
        gameObject.SetActive(true);

        DamagedEvent.AddListener(doorHitted);

        LoopSound.Play();
        LoopSound.Pause();
    }

    void doorHitted(DamageData damage)
    {
        TargetAnimator.Play("Hit", 0, 0);
    }

    private void Update()
    {
        if(!isMoving)
        {
            speed = Mathf.MoveTowards(speed, 0, Acceleration * Time.deltaTime);
        }

        Vector3 currentPosition = TargetTransform.localPosition;
        currentPosition += movementMask * speed * Time.deltaTime;
        for (int i = 0; i < 3; i++)
        {
            if (movementMask[i] == 0)
                continue;

            currentPosition[i] = Mathf.Clamp(currentPosition[i], MinPosition, MaxPosition);
        }
        TargetTransform.localPosition = currentPosition;

        float fraction = speed / MoveSpeed;
        fraction = Mathf.Abs(fraction);
        float volume = Mathf.Lerp(0, MaxVolume, LoopCurve.Evaluate(fraction));
        float pitch = Mathf.Lerp(MinPitch, MaxPitch, LoopCurve.Evaluate(fraction));

        if(volume == 0)
        {
            if (LoopSound.isPlaying)
                LoopSound.Pause();
        }
        else
        {
            if (!LoopSound.isPlaying)
                LoopSound.UnPause();
        }

        LoopSound.pitch = pitch;
        LoopSound.volume = volume;
    }

    public void MoveEnd()
    {
        isMoving = false;
        TargetAnimator.SetBool("IsMoving", isMoving);
    }

    public void Move(float direction)
    {
        if (!isMoving)
        {
            isMoving = true;
        }

        TargetAnimator.SetBool("IsMoving", isMoving);
        TargetAnimator.SetFloat("Direction", direction);

        if (!canMove(direction))
        {
            MoveEnd();
            return;
        }

        speed += Acceleration * Time.deltaTime * direction;

        speed = Mathf.Clamp(speed, -MoveSpeed, MoveSpeed);
    }

    bool canMove(float direction)
    {
        Vector3 currentPosition = TargetTransform.localPosition;
        for(int i = 0; i < 3; i++)
        {
            if (movementMask[i] < 1)
                continue;

            if (currentPosition[i] < MaxPosition && direction * movementMask[i] > 0)
                return true;
            else if (currentPosition[i] > MinPosition && direction * movementMask[i] < 0)
                return true;
        }

        return false;
    }
}
