﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BotEntity : CharacterEntity
{
    public enum Characteristic
    {
        Normal,
        NoneAttack
    }
    protected string botPlayerName;
    public override string playerName
    {
        get { return botPlayerName; }
        set
        {
            if (PhotonNetwork.isMasterClient)
            {
                botPlayerName = value;
                photonView.RPC("RpcUpdateBotName", PhotonTargets.AllBuffered, value);
            }
        }
    }
    public const float ReachedTargetDistance = 0.1f;
    public float updateMovementDuration = 2;
    public float attackDuration = 0;
    public float randomMoveDistance = 5f;
    public float detectEnemyDistance = 2f;
    public float turnSpeed = 5f;
    public Characteristic characteristic;
    public CharacterStats startAddStats;
    private Vector3 targetPosition;
    private float lastUpdateMovementTime;
    private float lastAttackTime;
    private bool isWallHit;

    protected override void Init()
    {
        base.Init();
    }

    protected override void Awake()
    {
        base.Awake();
        if (PhotonNetwork.isMasterClient)
        {
            ServerSpawn(false);
            lastUpdateMovementTime = Time.unscaledTime - updateMovementDuration;
            lastAttackTime = Time.unscaledTime - attackDuration;
        }
    }

    // Override to do nothing
    protected override void SetLocalPlayer()
    {
    }

    // Override to do nothing
    protected override void OnStartLocalPlayer()
    {
    }

    // Override to do nothing
    protected override void UpdateInput()
    {
    }

    protected override void UpdateMovements()
    {
        if (!PhotonNetwork.isMasterClient)
            return;
        
        if (Hp <= 0)
        {
            ServerRespawn(false);
            TempRigidbody.velocity = new Vector3(0, TempRigidbody.velocity.y, 0);
            return;
        }
        // Bots will update target movement when reached move target / hitting the walls / it's time
        var isReachedTarget = IsReachedTargetPosition();
        if (isReachedTarget || isWallHit || Time.unscaledTime - lastUpdateMovementTime >= updateMovementDuration)
        {
            lastUpdateMovementTime = Time.unscaledTime;
            targetPosition = new Vector3(
                TempTransform.position.x + Random.Range(-randomMoveDistance, randomMoveDistance),
                0,
                TempTransform.position.z + Random.Range(-randomMoveDistance, randomMoveDistance));
            isWallHit = false;
        }

        var rotatePosition = targetPosition;
        CharacterEntity enemy;
        if (FindEnemy(out enemy) && characteristic == Characteristic.Normal && Time.unscaledTime - lastAttackTime >= attackDuration)
        {
            lastAttackTime = Time.unscaledTime;
            if (CurrentEquippedWeapon.currentReserveAmmo > 0)
            {
                if (CurrentEquippedWeapon.currentAmmo == 0)
                    ServerReload();
                else if (attackingActionId < 0)
                    attackingActionId = WeaponData.GetRandomAttackAnimation().actionId;
            }
            else
            {
                if (WeaponData != null)
                {
                    var nextPosition = WeaponData.equipPosition + 1;
                    if (nextPosition < equippedWeapons.Length && !equippedWeapons[nextPosition].IsEmpty())
                        ServerChangeWeapon(nextPosition);
                }
                else
                    ServerChangeWeapon(selectWeaponIndex + 1);
            }
            rotatePosition = enemy.TempTransform.position;
        }
        else if (attackingActionId >= 0)
            attackingActionId = -1;

        // Gets a vector that points from the player's position to the target's.
        var heading = targetPosition - TempTransform.position;
        var distance = heading.magnitude;
        var direction = heading / distance; // This is now the normalized direction.
        Move(direction);
        // Rotate to target
        var rotateHeading = rotatePosition - TempTransform.position;
        var targetRotation = Quaternion.LookRotation(rotateHeading);
        TempTransform.rotation = Quaternion.Lerp(TempTransform.rotation, Quaternion.Euler(0, targetRotation.eulerAngles.y, 0), Time.deltaTime * turnSpeed);
    }

    private bool IsReachedTargetPosition()
    {
        return Vector3.Distance(targetPosition, TempTransform.position) < ReachedTargetDistance;
    }

    private bool FindEnemy(out CharacterEntity enemy)
    {
        enemy = null;
        var colliders = Physics.OverlapSphere(TempTransform.position, detectEnemyDistance);
        foreach (var collider in colliders)
        {
            var character = collider.GetComponent<CharacterEntity>();
            if (character != null && character != this && character.Hp > 0)
            {
                enemy = character;
                return true;
            }
        }
        return false;
    }

    protected override void OnCollisionStay(Collision collision)
    {
        base.OnCollisionStay(collision);
        if (collision.collider.tag == "Wall")
            isWallHit = true;
    }

    public override void OnSpawn()
    {
        base.OnSpawn();
        addStats += startAddStats;
        Hp = TotalHp;
    }

    [PunRPC]
    protected void RpcUpdateBotName(string name)
    {
        botPlayerName = name;
    }
}
