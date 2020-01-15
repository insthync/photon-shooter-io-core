﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Pun.UtilityScripts;
using Photon.Realtime;
using System.Linq;

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
            if (PhotonNetwork.IsMasterClient)
            {
                botPlayerName = value;
                photonView.RPC("RpcUpdateBotName", RpcTarget.All, value);
            }
        }
    }
    protected PunTeams.Team botPlayerTeam;
    public override PunTeams.Team playerTeam
    {
        get { return botPlayerTeam; }
        set
        {
            if (PhotonNetwork.IsMasterClient)
            {
                botPlayerTeam = value;
                photonView.RPC("RpcUpdateBotTeam", RpcTarget.Others, value);
            }
        }
    }
    public const float ReachedTargetDistance = 0.1f;
    public float updateMovementDuration = 2f;
    public float attackDuration = 0f;
    public float forgetEnemyDuration = 3f;
    public float randomDashDurationMin = 3f;
    public float randomDashDurationMax = 5f;
    public float randomMoveDistance = 5f;
    public float detectEnemyDistance = 2f;
    public float turnSpeed = 5f;
    public Characteristic characteristic;
    public CharacterStats startAddStats;
    [HideInInspector, System.NonSerialized]
    public bool isFixRandomMoveAroundPoint;
    [HideInInspector, System.NonSerialized]
    public Vector3 fixRandomMoveAroundPoint;
    [HideInInspector, System.NonSerialized]
    public float fixRandomMoveAroundDistance;
    private Vector3 targetPosition;
    private float lastUpdateMovementTime;
    private float lastAttackTime;
    private float randomDashDuration;
    private CharacterEntity enemy;

    protected override void Awake()
    {
        base.Awake();
        if (PhotonNetwork.IsMasterClient)
        {
            ServerSpawn(false);
            lastUpdateMovementTime = Time.unscaledTime - updateMovementDuration;
            lastAttackTime = Time.unscaledTime - attackDuration;
            randomDashDuration = dashDuration + Random.Range(randomDashDurationMin, randomDashDurationMax);
        }
    }

    protected override void SyncData()
    {
        if (!PhotonNetwork.IsMasterClient)
            return;
        base.SyncData();
        photonView.RPC("RpcUpdateBotName", RpcTarget.Others, botPlayerName);
        photonView.RPC("RpcUpdateBotTeam", RpcTarget.Others, botPlayerTeam);
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (!PhotonNetwork.IsMasterClient)
            return;
        base.OnPlayerEnteredRoom(newPlayer);
        photonView.RPC("RpcUpdateBotName", newPlayer, botPlayerName);
        photonView.RPC("RpcUpdateBotTeam", newPlayer, botPlayerTeam);
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
        if (!PhotonNetwork.IsMasterClient)
            return;

        if (Hp <= 0)
        {
            ServerRespawn(false);
            CacheRigidbody.velocity = new Vector3(0, CacheRigidbody.velocity.y, 0);
            return;
        }

        // Bots will update target movement when reached move target / hitting the walls / it's time
        var isReachedTarget = IsReachedTargetPosition();
        if (isReachedTarget || Time.unscaledTime - lastUpdateMovementTime >= updateMovementDuration)
        {
            lastUpdateMovementTime = Time.unscaledTime;
            if (enemy != null)
            {
                targetPosition = new Vector3(
                    enemy.CacheTransform.position.x + Random.Range(-1f, 1f) * detectEnemyDistance,
                    0,
                    enemy.CacheTransform.position.z + Random.Range(-1, 1f) * detectEnemyDistance);
            }
            else if (isFixRandomMoveAroundPoint)
            {
                targetPosition = new Vector3(
                    fixRandomMoveAroundPoint.x + Random.Range(-1f, 1f) * fixRandomMoveAroundDistance,
                    0,
                    fixRandomMoveAroundPoint.z + Random.Range(-1f, 1f) * fixRandomMoveAroundDistance);
            }
            else
            {
                targetPosition = new Vector3(
                    CacheTransform.position.x + Random.Range(-1f, 1f) * randomMoveDistance,
                    0,
                    CacheTransform.position.z + Random.Range(-1f, 1f) * randomMoveDistance);
            }
        }

        var rotatePosition = targetPosition;
        if (enemy == null || enemy.IsDead || Time.unscaledTime - lastAttackTime >= forgetEnemyDuration)
        {
            // Try find enemy. If found move to target in next frame
            if (FindEnemy(out enemy))
            {
                lastAttackTime = Time.unscaledTime;
                lastUpdateMovementTime = Time.unscaledTime - updateMovementDuration;
            }
        }
        else
        {
            // Set target rotation to enemy position
            rotatePosition = enemy.CacheTransform.position;
        }

        attackingActionId = -1;
        if (enemy != null)
        {
            if (characteristic == Characteristic.Normal)
            {
                if (Time.unscaledTime - lastAttackTime >= attackDuration &&
                    Vector3.Distance(enemy.CacheTransform.position, CacheTransform.position) < GetAttackRange())
                {
                    // Attack when nearby enemy
                    attackingActionId = WeaponData.GetRandomAttackAnimation().actionId;
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
                }
            }
        }

        // Dashing
        if (Time.unscaledTime - dashingTime >= randomDashDuration && !isDashing)
        {
            randomDashDuration = dashDuration + Random.Range(randomDashDurationMin, randomDashDurationMax);
            isDashing = true;
        }

        // Gets a vector that points from the player's position to the target's.
        if (!IsReachedTargetPosition())
            Move((targetPosition - CacheTransform.position).normalized);

        if (IsReachedTargetPosition())
        {
            targetPosition = CacheTransform.position + (CacheTransform.forward * ReachedTargetDistance / 2f);
            CacheRigidbody.velocity = new Vector3(0, CacheRigidbody.velocity.y, 0);
        }
        // Rotate to target
        var rotateHeading = rotatePosition - CacheTransform.position;
        var targetRotation = Quaternion.LookRotation(rotateHeading);
        CacheTransform.rotation = Quaternion.Lerp(CacheTransform.rotation, Quaternion.Euler(0, targetRotation.eulerAngles.y, 0), Time.deltaTime * turnSpeed);
        UpdateStatPoint();
    }

    private void UpdateStatPoint()
    {
        if (statPoint <= 0)
            return;
        var dict = new Dictionary<CharacterAttributes, int>();
        var list = GameplayManager.Singleton.attributes.Values.ToList();
        foreach (var entry in list)
        {
            dict.Add(entry, entry.randomWeight);
        }
        CmdAddAttribute(WeightedRandomizer.From(dict).TakeOne().name);
    }

    private bool IsReachedTargetPosition()
    {
        if (enemy != null)
            return Vector3.Distance(targetPosition, CacheTransform.position) < Mathf.Min(enemy.CacheCollider.bounds.size.x, enemy.CacheCollider.bounds.size.z);
        return Vector3.Distance(targetPosition, CacheTransform.position) < ReachedTargetDistance;
    }

    private bool FindEnemy(out CharacterEntity enemy)
    {
        enemy = null;
        var gameplayManager = GameplayManager.Singleton;
        var colliders = Physics.OverlapSphere(CacheTransform.position, detectEnemyDistance);
        foreach (var collider in colliders)
        {
            var character = collider.GetComponent<CharacterEntity>();
            if (character != null && character != this && character.Hp > 0 && gameplayManager.CanReceiveDamage(character, this))
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
        if (collision.collider.CompareTag("Wall"))
        {
            // Find another position to move in next frame
            lastUpdateMovementTime = Time.unscaledTime - updateMovementDuration;
        }
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

    [PunRPC]
    protected void RpcUpdateBotTeam(PunTeams.Team team)
    {
        botPlayerTeam = team;
    }
}
