using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NewDamage : MonoBehaviour
{
    public float damage = 10f;
    private void OnTriggerEnter(Collider other)
    {
            ICanBeDamaged canBeDamaged = other.GetComponent<ICanBeDamaged>();
            if(canBeDamaged != null) canBeDamaged.TakeDamage(damage);
    }
}
