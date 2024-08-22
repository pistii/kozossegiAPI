﻿using Google.Protobuf.WellKnownTypes;
using KozoskodoAPI.Auth;
using KozoskodoAPI.Auth.Helpers;
using KozoskodoAPI.Data;
using KozoskodoAPI.Models;
using KozoskodoAPI.Realtime.Connection;
using KozoskodoAPI.Repo;
using KozossegiAPI.Models;
using KozossegiAPI.Models.Cloud;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace KozoskodoAPI.Realtime
{
    [Authorize]
    public class ChatHub : ConnectionHandler<IChatClient>
    {
        private readonly ConnectionHandler<IChatClient> _connectionHandler;
        private readonly IMapConnections _connections;
        private readonly IFriendRepository _friendRepo;

        public ChatHub(IJwtUtils utils, IMapConnections mapConnections, DBContext context, IFriendRepository friendRepository)
        : base(utils, mapConnections, context) // Öröklés a szülőosztályból, meg kell hívni a konstruktorát
        {
            _connectionHandler = this;
            _connections = mapConnections;
            _friendRepo = friendRepository;
        }

        public async Task ReceiveMessage(int fromId, int userId, string message, FileUpload? fileUpload)
        {
            await Clients.User(userId.ToString()).ReceiveMessage(fromId, userId, message, fileUpload);
        }

        public async Task SendMessage(int fromId, int userId, string message)
        {
            foreach (var user in _connections.GetConnectionsById(userId))
            {
                await Clients.Client(user).ReceiveMessage(fromId, userId, message);
            }
        }

        public async Task SendStatusInfo(int messageId, int userId, int status)
        {
            foreach (var user in _connections.GetConnectionsById(userId))
            {
                await Clients.Client(user).SendStatusInfo(messageId, status);
            }
        }


        public async Task ReceiveOnlineFriends(int userId)
        {
            List<Personal_IsOnlineDto> onlineFriends = new List<Personal_IsOnlineDto>();
            var friends = await _friendRepo.GetAllFriendAsync(userId);

            foreach (var friend in friends)
            {
                if (_connections.ContainsUser(friend.id))
                {
                    //Ha engedélyezte az online státuszt                    
                    if (friend.users.isOnlineEnabled)
                    {
                        Personal_IsOnlineDto dto = new Personal_IsOnlineDto(friend, friend.users.isOnlineEnabled);
                        onlineFriends.Add(dto);
                    }
                }
            }
            foreach (var user in _connections.GetConnectionsById(userId))
            {
                await _connectionHandler.Clients.Client(user).ReceiveOnlineFriends(onlineFriends);
            }
        }
    }
}
