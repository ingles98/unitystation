﻿using System;
using System.Collections.Generic;
using DatabaseAPI;
using Mirror;
using UnityEngine;

namespace AdminTools
{
	public class PlayerAlerts : MonoBehaviour
	{
		[SerializeField] private GameObject playerAlertsWindow;
		[SerializeField] private PlayerAlertsScroll playerAlertsScroll;
		[SerializeField] private GUI_Notification notifications;
		private const string NotificationKey = "playeralert";


		private List<PlayerAlertData> serverPlayerAlerts
			= new List<PlayerAlertData>();

		private List<PlayerAlertData> clientPlayerAlerts
			= new List<PlayerAlertData>();


		public void LoadAllEntries(List<PlayerAlertData> alertEntries)
		{
			playerAlertsScroll.LoadAlertEntries(alertEntries);
		}

		public void AppendEntries(List<PlayerAlertData> alertEntries)
		{
			playerAlertsScroll.AppendAlertEntries(alertEntries);
		}

		void OnEnable()
		{
			playerAlertsWindow.SetActive(false);
		}

		public void ClearAllNotifications()
		{
			notifications.ClearAll();
		}

		public void UpdateNotifications(int amt)
		{
			notifications.AddNotification(NotificationKey, amt);
		}

		public void ClearLogs()
		{
			serverPlayerAlerts.Clear();
			clientPlayerAlerts.Clear();
			notifications.ClearAll();
		}

		public void ClientUpdateAlertLog(string data)
		{
			if (string.IsNullOrEmpty(data)) return;

			var update = JsonUtility.FromJson<PlayerAlertsUpdate>(data);
			clientPlayerAlerts.AddRange(update.playerAlerts);
			LoadAllEntries(clientPlayerAlerts);
		}

		public void ClientUpdateSingleEntry(PlayerAlertData entry)
		{
			var index = clientPlayerAlerts.FindIndex(x =>
				x.playerNetId == entry.playerNetId && x.roundTime == entry.roundTime);
			if (index == -1)
			{
				playerAlertsScroll.AddNewPlayerAlert(entry);
			}
			else
			{
				playerAlertsScroll.UpdateExistingPlayerAlert(entry);
			}
		}

		public void ServerRequestEntries(string userId, int count, NetworkConnection requestee)
		{
			if (!PlayerList.Instance.IsAdmin(userId)) return;

			if (count >=  serverPlayerAlerts.Count)
			{
				return;
			}

			PlayerAlertsUpdate update = new PlayerAlertsUpdate();

			update.playerAlerts = serverPlayerAlerts;

			PlayerAlertsUpdateMessage.SendLogUpdateToAdmin(requestee, update);
			if (notifications.notifications.ContainsKey(NotificationKey))
			{
				PlayerAlertNotifications.Send(requestee, notifications.notifications[NotificationKey]);
			}
		}

		public void ServerAddNewEntry(string incidentTime, PlayerAlertTypes alertType, ConnectedPlayer perp,
			string message)
		{
			if (perp == null) return;
			var entry = new PlayerAlertData();
			entry.roundTime = incidentTime;
			entry.playerNetId = perp.Connection.identity.netId;
			entry.playerAlertType = alertType;
			entry.Message = message;
			serverPlayerAlerts.Add(entry);
			PlayerAlertNotifications.SendToAll(1);
			ServerSendEntryToAllAdmins(entry);
		}

		public void ServerSendEntryToAllAdmins(PlayerAlertData entry)
		{
			PlayerAlertsUpdateMessage.SendSingleEntryToAdmins(entry);
		}

		public void ServerProcessActionRequest(string adminId, PlayerAlertActions actionRequest,
			string roundTimeOfIncident, uint perpId, string adminToken)
		{
			if (!PlayerList.Instance.IsAdmin(adminId)) return;

			var admin = PlayerList.Instance.GetAdmin(adminId, adminToken);
			if (admin == null) return;

			var index = serverPlayerAlerts.FindIndex(x =>
				x.playerNetId == perpId && x.roundTime == roundTimeOfIncident);
			if (index == -1)
			{
				Logger.Log($"Could not find perp id {perpId} with roundTime incident: {roundTimeOfIncident}");
				return;
			}

			if (!NetworkIdentity.spawned.ContainsKey(perpId))
			{
				Logger.Log($"Perp id {perpId} not found in Spawnlist");
				return;
			}

			var perp = NetworkIdentity.spawned[perpId];

			switch (actionRequest)
			{
				case PlayerAlertActions.Gibbed:
					ProcessGibRequest(perp.gameObject, admin, serverPlayerAlerts[index], adminId);
					break;
				case PlayerAlertActions.TakenCareOf:
					ProcessTakenCareOfRequest(perp.gameObject, admin, serverPlayerAlerts[index], adminId);
					break;
			}
		}

		private void ProcessGibRequest(GameObject perp, GameObject admin, PlayerAlertData alertEntry, string adminId)
		{
			if (alertEntry.gibbed) return;

			var playerScript = perp.GetComponent<PlayerScript>();
			if (playerScript == null || playerScript.IsGhost || playerScript.playerHealth == null) return;

			UIManager.Instance.adminChatWindows.adminToAdminChat.ServerAddChatRecord($"{admin.ExpensiveName()} BRUTALLY GIBBED player {perp.ExpensiveName()} for a " +
			                                                                         $"{alertEntry.playerAlertType.ToString()} incident that happened at roundtime: " +
			                                                                         $"{alertEntry.roundTime}", adminId);

			playerScript.playerHealth.ServerGibPlayer();

			alertEntry.gibbed = true;
			ServerSendEntryToAllAdmins(alertEntry);
			notifications.AddNotification(NotificationKey, -1);
			PlayerAlertNotifications.SendToAll(-1);
		}

		private void ProcessTakenCareOfRequest(GameObject perp, GameObject admin, PlayerAlertData alertEntry, string adminId)
		{
			if (alertEntry.takenCareOf) return;

			var playerScript = perp.GetComponent<PlayerScript>();
			if (playerScript == null || playerScript.IsGhost || playerScript.playerHealth == null) return;

			UIManager.Instance.adminChatWindows.adminToAdminChat.ServerAddChatRecord($"{admin.ExpensiveName()} is talking to or monitoring player {perp.ExpensiveName()} for a " +
			                                                                         $"{alertEntry.playerAlertType.ToString()} incident that happened at roundtime: " +
			                                                                         $"{alertEntry.roundTime}", adminId);

			alertEntry.takenCareOf = true;
			ServerSendEntryToAllAdmins(alertEntry);
			notifications.AddNotification(NotificationKey, -1);
			PlayerAlertNotifications.SendToAll(-1);
		}

		public void ToggleWindow()
		{
			if (!playerAlertsWindow.activeInHierarchy)
			{
				playerAlertsWindow.SetActive(true);
				notifications.ClearAll();
				AdminCheckPlayerAlerts.Send(ServerData.UserID, clientPlayerAlerts.Count);
			}
			else
			{
				playerAlertsWindow.SetActive(false);
			}
		}
	}

	public enum PlayerAlertTypes
	{
		RDM,
		PlasmaOpen
	}

	public enum PlayerAlertActions
	{
		Gibbed,
		TakenCareOf,
	}

	[Serializable]
	public class PlayerAlertsUpdate
	{
		public List<PlayerAlertData> playerAlerts = new List<PlayerAlertData>();
	}

	[Serializable]
	public class PlayerAlertData : ChatEntryData
	{
		public PlayerAlertTypes playerAlertType;
		public uint playerNetId;
		public string roundTime;
		public bool takenCareOf;
		public bool gibbed;
	}
}