/* 25 April 2019
 * 
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading;
using log4net;
using Nini.Config;
using Mono.Addins;
using OpenMetaverse;
using OpenMetaverse.Messages.Linden;
using OpenMetaverse.Packets;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using Caps=OpenSim.Framework.Capabilities.Caps;
using OSDqueue = OpenSim.Framework.NSimpleChain<OpenMetaverse.StructuredData.OSD>;

namespace OpenSim.Region.ClientStack.Linden
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "EventQueueGetModule")]
    public class EventQueueGetModule : IEventQueue, INonSharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static string LogHeader = "[EVENT QUEUE GET MODULE]";

        /// <value>
        /// Debug level.
        /// </value>
        public int DebugLevel { get; set; }

        // Viewer post requests timeout in 60 secs
        // https://bitbucket.org/lindenlab/viewer-release/src/421c20423df93d650cc305dc115922bb30040999/indra/llmessage/llhttpclient.cpp?at=default#cl-44
        //
        private const int VIEWER_TIMEOUT = 60 * 1000;
        // Just to be safe, we work on a 10 sec shorter cycle
        private const int SERVER_EQ_TIME_NO_EVENTS = VIEWER_TIMEOUT - (10 * 1000);

        protected Scene m_scene;

        private Dictionary<UUID, int> m_ids = new Dictionary<UUID, int>();


        private Dictionary<UUID, OSDqueue> queues = new Dictionary<UUID, OSDqueue>();
        private Dictionary<UUID, UUID> m_AvatarQueueUUIDMapping = new Dictionary<UUID, UUID>();

        #region INonSharedRegionModule methods
        public virtual void Initialise(IConfigSource config)
        {
        }

        public void AddRegion(Scene scene)
        {
            m_scene = scene;
            scene.RegisterModuleInterface<IEventQueue>(this);

            scene.EventManager.OnClientClosed += ClientClosed;
            scene.EventManager.OnRegisterCaps += OnRegisterCaps;

            MainConsole.Instance.Commands.AddCommand(
                "Debug",
                false,
                "debug eq",
                "debug eq [0|1|2]",
                "Turn on event queue debugging\n"
                    + "  <= 0 - turns off all event queue logging\n"
                    + "  >= 1 - turns on event queue setup and outgoing event logging\n"
                    + "  >= 2 - turns on poll notification",
                HandleDebugEq);

            MainConsole.Instance.Commands.AddCommand(
                "Debug",
                false,
                "show eq",
                "show eq",
                "Show contents of event queues for logged in avatars.  Used for debugging.",
                HandleShowEq);
        }

        public void RemoveRegion(Scene scene)
        {
            if (m_scene != scene)
                return;

            scene.EventManager.OnClientClosed -= ClientClosed;
            scene.EventManager.OnRegisterCaps -= OnRegisterCaps;

            scene.UnregisterModuleInterface<IEventQueue>(this);
            m_scene = null;
        }

        public void RegionLoaded(Scene scene)
        {
        }

        public virtual void Close()
        {
        }

        public virtual string Name
        {
            get { return "EventQueueGetModule"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        #endregion

        protected void HandleDebugEq(string module, string[] args)
        {
            int debugLevel;

            if (!(args.Length == 3 && int.TryParse(args[2], out debugLevel)))
            {
                MainConsole.Instance.OutputFormat("Usage: debug eq [0|1|2]");
            }
            else
            {
                DebugLevel = debugLevel;
                MainConsole.Instance.OutputFormat(
                    "Set event queue debug level to {0} in {1}", DebugLevel, m_scene.RegionInfo.RegionName);
            }
        }
        
        protected void HandleShowEq(string module, string[] args)
        {
            MainConsole.Instance.OutputFormat("For scene {0}", m_scene.Name);
            lock (queues)
            {
                foreach (KeyValuePair<UUID, OSDqueue> kvp in queues)
                {
                    if (kvp.Value.isEmpty)
                    {
                        MainConsole.Instance.OutputFormat(
                            "For agent {0} there are NO messages queued for send.",
                            kvp.Key);                      
                    }
                    else
                    {
                        MainConsole.Instance.OutputFormat(
                            "For agent {0} there are some messages queued for send.",
                            kvp.Key);
                    }
                }
            }
        }
        
        #region IEventQueue Members

        public bool Enqueue(OSD ev, UUID avatarID)
        {
            try
            {
                lock (queues)
                {
                    OSDqueue queue;
					if (queues.TryGetValue(avatarID, out queue))
                    {
                        queue.Enqueue(ev);
                        return true;
                    }
                }

                OSDMap evMap = (OSDMap)ev;
                m_log.WarnFormat( "[EVENTQUEUE]: (Enqueue) No queue found for agent {0} when placing message {1} in region {2}",
                                    avatarID, evMap["message"], m_scene.Name);
            }
            catch (NullReferenceException e)
            {
                m_log.Error("[EVENTQUEUE] Caught exception: " + e);
                return false;
            }

            return true;
        }

        #endregion

        private void ClientClosed(UUID agentID, Scene scene)
        {
            lock (queues)
            {
                queues.Remove(agentID);
            }

            lock (m_AvatarQueueUUIDMapping)
                  m_AvatarQueueUUIDMapping.Remove(agentID);

            lock (m_ids)
                  m_ids.Remove(agentID);
        }

        /// <summary>
        /// Generate an Event Queue Get handler path for the given eqg uuid.
        /// </summary>
        /// <param name='eqgUuid'></param>
        private string GenerateEqgCapPath(UUID eqgUuid)
        {
            return string.Format("/CAPS/EQG/{0}/", eqgUuid);
        }

        public void OnRegisterCaps(UUID agentID, Caps caps)
        {
            // Register an event queue for the client
            if (DebugLevel > 0)
                m_log.DebugFormat(
                    "[EVENTQUEUE]: OnRegisterCaps: agentID {0} caps {1} region {2}",
                    agentID, caps, m_scene.RegionInfo.RegionName);

            UUID eventQueueGetUUID;
            OSDqueue queue;
            Random rnd = new Random(Environment.TickCount);
            int nrnd = rnd.Next(30000000);
            if (nrnd < 0)
                nrnd = -nrnd;

            lock (queues)
            {
                if (queues.ContainsKey(agentID))
                {
                    queue = queues[agentID];
                }
                else
                    queue = null;

                if (queue == null)
                {
                    queue = new OSDqueue();
                    queues[agentID] = queue;

                    // push markers to handle old responses still waiting
                    // this will cost at most viewer getting two forced noevents
                    // even being a new queue better be safe
                    queue.Enqueue(null);
                    queue.Enqueue(null); // one should be enough

                    lock (m_AvatarQueueUUIDMapping)
                    {
                        eventQueueGetUUID = UUID.Random();
                        if (m_AvatarQueueUUIDMapping.ContainsKey(agentID))
                        {
                            // oops this should not happen ?
                            m_log.DebugFormat("[EVENTQUEUE]: Found Existing UUID without a queue");
                            eventQueueGetUUID = m_AvatarQueueUUIDMapping[agentID];
                        }
                        m_AvatarQueueUUIDMapping.Add(agentID, eventQueueGetUUID);
                    }
                    lock (m_ids)
                    {
                        if (!m_ids.ContainsKey(agentID))
                            m_ids.Add(agentID, nrnd);
                        else
                            m_ids[agentID] = nrnd;
                    }
                }
                else
                {
                    // push markers to handle old responses still waiting
                    // this will cost at most viewer getting two forced noevents
                    // even being a new queue better be safe
                    queue.Enqueue(null);
                    queue.Enqueue(null); // one should be enough

                    // reuse or not to reuse TODO FIX
                    lock (m_AvatarQueueUUIDMapping)
                    {
                        // Reuse open queues.  The client does!
                        // Its reuse caps path not queues those are been reused already
                        if (m_AvatarQueueUUIDMapping.ContainsKey(agentID))
                        {
                            m_log.DebugFormat("[EVENTQUEUE]: Found Existing UUID!");
                            eventQueueGetUUID = m_AvatarQueueUUIDMapping[agentID];
                        }
                        else
                        {
                            eventQueueGetUUID = UUID.Random();
                            m_AvatarQueueUUIDMapping.Add(agentID, eventQueueGetUUID);
                            m_log.DebugFormat("[EVENTQUEUE]: Using random UUID!");
                        }
                    }
                    lock (m_ids)
                    {
                        // change to negative numbers so they are changed at end of sending first marker
                        // old data on a queue may be sent on a response for a new caps
                        // but at least will be sent with coerent IDs
                        if (!m_ids.ContainsKey(agentID))
                            m_ids.Add(agentID, -nrnd); // should not happen
                        else
                            m_ids[agentID] = -m_ids[agentID];
                    }
                }
            }

            caps.RegisterPollHandler(
                "EventQueueGet",
                new PollServiceEventArgs(null, GenerateEqgCapPath(eventQueueGetUUID), HasEvents, GetEvents, NoEvents, null, agentID, SERVER_EQ_TIME_NO_EVENTS));
        }

        public bool HasEvents(UUID requestID, UUID agentID)
        {
            OSDqueue queue;
            lock (queues)
            {
                if (queues.TryGetValue(agentID, out queue))
                    return !queue.isEmpty;
            }

            return true; // Polling for unknown agent.
        }

        /// <summary>
        /// Logs a debug line for an outbound event queue message if appropriate.
        /// </summary>
        /// <param name='element'>Element containing message</param>
        private void LogOutboundDebugMessage(OSD element, UUID agentId)
        {
            if (element is OSDMap)
            {
                OSDMap ev = (OSDMap)element;
                m_log.DebugFormat(
                    "Eq OUT {0,-30} to {1,-20} {2,-20}",
                    ev["message"], m_scene.GetScenePresence(agentId).Name, m_scene.Name);
            }
        }

        public void Drop(UUID requestID, UUID pAgentId)
        {
            // do nothing for now, hope client close will do it
        }

        public Hashtable GetEvents(UUID requestID, UUID pAgentId)
        {
            OSDqueue queue;
            lock (queues)
            {
                if (queues.TryGetValue(pAgentId, out queue))
                {
                    if (queue == null)
                        return NoEvents(requestID, pAgentId);

                    if (queue.isEmpty)
                        return NoEvents(requestID, pAgentId);

                    // we took this queue and now replace it with an empty one.
                    queues[pAgentId] = new OSDqueue();
                }
                else
                {
                    return NoEvents(requestID, pAgentId);
                }
            }

            OSDArray array = new OSDArray();
            int thisID = 0;
            bool negativeID = false;

            lock (m_ids)
                  thisID = m_ids[pAgentId];

            if (thisID < 0)
            {
                negativeID = true;
                thisID = -thisID;
            }

            OSD element = null;

            while (queue.Dequeue( out element ))
            {
                // add elements until a marker is found
                // so they get into a response
                if (element == null)
                    break;
                array.Add(element);
                thisID++;
            }

            OSDMap events = null;

            if (array.Count > 0)
            {
                events = new OSDMap();
                events.Add("events", array);
                events.Add("id", new OSDInteger(thisID));
            }

            if (negativeID && element == null)
            {
                Random rnd = new Random(Environment.TickCount);
                thisID = rnd.Next(30000000);
                if (thisID < 0)
                    thisID = -thisID;
            }

            lock (m_ids)
            {
                m_ids[pAgentId] = thisID + 1;
            }

            // if there where no elements before a marker send a NoEvents
            if (array.Count == 0)
                return NoEvents(requestID, pAgentId);

            Hashtable responsedata = new Hashtable();
            responsedata["int_response_code"] = 200;
            responsedata["content_type"] = "application/xml";
            responsedata["keepalive"] = false;
            responsedata["reusecontext"] = false;
            responsedata["str_response_string"] = OSDParser.SerializeLLSDXmlString(events);
            return responsedata;
        }

        public Hashtable NoEvents(UUID requestID, UUID agentID)
        {
            Hashtable responsedata = new Hashtable();
            responsedata["int_response_code"] = 502;
            responsedata["content_type"] = "text/plain";
            responsedata["keepalive"] = false;
            responsedata["reusecontext"] = false;
            responsedata["str_response_string"] = "<llsd></llsd>";
            responsedata["error_status_text"] = "<llsd></llsd>";
            responsedata["http_protocol_version"] = "HTTP/1.0";
            return responsedata;
        }

        public virtual void EnableSimulator(ulong handle, IPEndPoint endPoint, UUID avatarID, int regionSizeX, int regionSizeY)
        {
            if (DebugLevel > 0)
                m_log.DebugFormat("{0} EnableSimulator. handle={1}, endPoint={2}, avatarID={3}",
                    LogHeader, handle, endPoint, avatarID, regionSizeX, regionSizeY);

            OSD item = EventQueueHelper.EnableSimulator(handle, endPoint, regionSizeX, regionSizeY);
            Enqueue(item, avatarID);
        }

        public virtual void EstablishAgentCommunication(UUID avatarID, IPEndPoint endPoint, string capsPath,
                                ulong regionHandle, int regionSizeX, int regionSizeY)
        {
            if (DebugLevel > 0)
                m_log.DebugFormat("{0} EstablishAgentCommunication. handle={1}, endPoint={2}, avatarID={3}",
                    LogHeader, regionHandle, endPoint, avatarID, regionSizeX, regionSizeY);

            OSD item = EventQueueHelper.EstablishAgentCommunication(avatarID, endPoint.ToString(), capsPath, regionHandle, regionSizeX, regionSizeY);
            Enqueue(item, avatarID);
        }

        public virtual void TeleportFinishEvent(ulong regionHandle, byte simAccess,
                                        IPEndPoint regionExternalEndPoint,
                                        uint locationID, uint flags, string capsURL,
                                        UUID avatarID, int regionSizeX, int regionSizeY)
        {
            if (DebugLevel > 0)
                m_log.DebugFormat("{0} TeleportFinishEvent. handle={1}, endPoint={2}, avatarID={3}",
                    LogHeader, regionHandle, regionExternalEndPoint, avatarID, regionSizeX, regionSizeY);

            OSD item = EventQueueHelper.TeleportFinishEvent(regionHandle, simAccess, regionExternalEndPoint,
                                                            locationID, flags, capsURL, avatarID, regionSizeX, regionSizeY);
            Enqueue(item, avatarID);
        }

        public virtual void CrossRegion(ulong handle, Vector3 pos, Vector3 lookAt,
                                IPEndPoint newRegionExternalEndPoint,
                                string capsURL, UUID avatarID, UUID sessionID, int regionSizeX, int regionSizeY)
        {
            if (DebugLevel > 0)
                m_log.DebugFormat("{0} CrossRegion. handle={1}, avatarID={2}, regionSize={3},{4}>",
                    LogHeader, handle, avatarID, regionSizeX, regionSizeY);

            OSD item = EventQueueHelper.CrossRegion(handle, pos, lookAt, newRegionExternalEndPoint,
                                                    capsURL, avatarID, sessionID, regionSizeX, regionSizeY);
            Enqueue(item, avatarID);
        }

        public void ChatterboxInvitation(UUID sessionID, string sessionName,
                                         UUID fromAgent, string message, UUID toAgent, string fromName, byte dialog,
                                         uint timeStamp, bool offline, int parentEstateID, Vector3 position,
                                         uint ttl, UUID transactionID, bool fromGroup, byte[] binaryBucket)
        {
            OSD item = EventQueueHelper.ChatterboxInvitation(sessionID, sessionName, fromAgent, message, toAgent, fromName, dialog,
                                                             timeStamp, offline, parentEstateID, position, ttl, transactionID,
                                                             fromGroup, binaryBucket);
            Enqueue(item, toAgent);
        }

        public void ChatterBoxSessionAgentListUpdates(UUID sessionID, UUID fromAgent, UUID toAgent, bool canVoiceChat,
                                                      bool isModerator, bool textMute, bool isEnterorLeave)
        {
            OSD item = EventQueueHelper.ChatterBoxSessionAgentListUpdates(sessionID, fromAgent, canVoiceChat,
                                                                          isModerator, textMute, isEnterorLeave);
            Enqueue(item, toAgent);
        }

        public void ChatterBoxForceClose(UUID toAgent, UUID sessionID, string reason)
        {
            OSD item = EventQueueHelper.ChatterBoxForceClose(sessionID, reason);

            Enqueue(item, toAgent);
        }

        public void ParcelProperties(ParcelPropertiesMessage parcelPropertiesMessage, UUID avatarID)
        {
            OSD item = EventQueueHelper.ParcelProperties(parcelPropertiesMessage);
            Enqueue(item, avatarID);
        }

        public void GroupMembershipData(UUID receiverAgent, GroupMembershipData[] data)
        {
            OSD item = EventQueueHelper.GroupMembershipData(receiverAgent, data);
            Enqueue(item, receiverAgent);
        }

        public void QueryReply(PlacesReplyPacket groupUpdate, UUID avatarID)
        {
            OSD item = EventQueueHelper.PlacesQuery(groupUpdate);
            Enqueue(item, avatarID);
        }

        public OSD ScriptRunningEvent(UUID objectID, UUID itemID, bool running, bool mono)
        {
            return EventQueueHelper.ScriptRunningReplyEvent(objectID, itemID, running, mono);
        }

        public OSD BuildEvent(string eventName, OSD eventBody)
        {
            return EventQueueHelper.BuildEvent(eventName, eventBody);
        }

        public void partPhysicsProperties(uint localID, byte physhapetype,
                        float density, float friction, float bounce, float gravmod,UUID avatarID)
        {
            OSD item = EventQueueHelper.partPhysicsProperties(localID, physhapetype,
                        density, friction, bounce, gravmod);
            Enqueue(item, avatarID);
        }
    }
}
