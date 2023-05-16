using System.Collections;
using System.Collections.Generic;
using System.Numerics;

public class RTSSolider
{
    public int owner;
    public bool walking;
    public Vector2 target;
    public Vector2 current;
    public float speed;
    
    public RTSSolider(Vector2 startPosition, int owner, float speed)
    {
        walking = false;
        target = startPosition;
        current = startPosition;
        this.speed = speed;
        this.owner = owner;
    }

    public void Update()
    {
        if (walking)
        {
            float distance = Vector2.Distance(current, target);
            Vector2 move = Vector2.Normalize(target - current);
            if (distance < speed)
            {
                move *= speed - distance;
                walking = false;
            }
            else
            {
                move *= speed;
            }
            current += move;
        }
    }

    public void SetTarget(Vector2 target)
    {
        if (this.target == target) return;
        walking = true;
        this.target = target;
    }
}
