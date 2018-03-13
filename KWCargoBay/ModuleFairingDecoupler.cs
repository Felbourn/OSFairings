/* ModuleFairingDecoupler
 * by Bob Fitch aka Felbourn
 * http://creativecommons.org/licenses/by-nc-sa/4.0/
 */
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/* Caveats (this is a temporary, hacked solution):
 *  - fairings inside fairings will not work -- the inner fairing will be marked unshielded when the outer fairing opens
 *  - things sticking out of the fairing will be shielded whether it appears they should or not
 *  - attaching a single fairing cone will shield everything as if all cone pieces were attached
 *  - decoupling a single cone piece will unshield the entire payload and ignore any other fairing cone parts
 */
namespace Felbourn
{
    public class ModuleFairingDecoupler : ModuleAnchoredDecoupler
    {
        [KSPField(guiName = "Old School Fairing", guiActive = true)]
        private bool shielding = false;

        [KSPField]
        public string payloadPreNode = "none";
        [KSPField]
        public string payloadNode = "top";
        [KSPField]
        public string payloadNode2 = "top2";
        [KSPField]
        public bool enableLogging = true;

        private List<Part> shieldedParts = new List<Part>();
        private List<string> exempt = new List<String>();

        private void Log(LogType mode, string message)
        {
            message = "[OSF] " + message;
            if (mode == LogType.Warning)
                Debug.LogWarning(message);
            else if (mode == LogType.Error)
                Debug.LogError(message);
            else if (!enableLogging)
                return;
            else
                Debug.Log(message);
        }

        /*public override void OnLoad(ConfigNode node)
        {
            ConfigNode exempts = node.GetNode("EXEMPT");
            if (exempts == null)
            {
                Log(LogType.Log, "no EXEMPT in " + node.name);
                return;
            }

            foreach (ConfigNode.Value one in exempts.values)
            {
                if (one.name != "part")
                {
                    Log(LogType.Warning, "unknown EXEMPT key: " + one.name);
                    continue;
                }
                Log(LogType.Log, "EXEMPT key: " + one.name);
                exempt.Add(one.value);
            }
        }*/

        public override void OnStart(PartModule.StartState state)
        {
            Log(LogType.Log, "OnStart " + state);
            base.OnStart(state);

            Log(LogType.Log, "fairing is '" + part.partInfo.name + "' attaching via explosiveNodeID 'node_stack_" + explosiveNodeID + "'");

            // I'm a nose fairing on a decoupler. Find my decoupler.
            AttachNode decouplerAttach = part.FindAttachNode(explosiveNodeID);
            if (decouplerAttach == null)
            {
                Log(LogType.Error, "can't find explosiveNodeID: node_stack_" + explosiveNodeID);
                return;
            }

            Part decoupler = decouplerAttach.attachedPart;
            if (decoupler == null)
            {
                Log(LogType.Warning, "not attached to anything, can't shield anything");
                return;
            }

            if (payloadPreNode != "none")
            {
                Log(LogType.Log, "fairing attached to '" + decoupler.partInfo.name + "' structure piece");
                AttachNode[] payloadPreAttach = decoupler.FindAttachNodes(payloadPreNode);
                if (payloadPreAttach == null)
                {
                    Log(LogType.Error, "can't find decoupler on structure: node_stack_" + payloadPreNode);
                    return;
                }
                decoupler = payloadPreAttach[0].attachedPart;
            }
            ShieldFrom(decoupler, payloadNode, requireNode: true);
            ShieldFrom(decoupler, payloadNode2, requireNode: false);
        }

        private void ShieldFrom(Part decoupler, string startNode, bool requireNode)
        {
            Log(LogType.Log, "fairing attached to '" + decoupler.partInfo.name + "' which should have payload on 'node_stack_" + startNode + "'");
            shieldedParts.Add(decoupler); // add decoupler so we don't search through it

            // I know the decoupler, so find its payload(s).
            AttachNode[] payloadAttach = decoupler.FindAttachNodes(startNode);
            if (payloadAttach == null)
            {
                if (requireNode)
                    Log(LogType.Error, "can't find payload node: node_stack_" + startNode);
                return;
            }
            Log(LogType.Log, payloadAttach.Length + " of 'node_stack_" + startNode + "' found, searching all");

            for (int i = payloadAttach.Length - 1; i >= 0; i--) // handle all same-name nodes that can have payload
            {
                Log(LogType.Log, "search payload mount " + decoupler.name + ".node_stack_" + startNode + "[" + i + "]");
                if (payloadAttach[i] == null || payloadAttach[i].attachedPart == null)
                {
                    Log(LogType.Log, "skip, " + decoupler.name + ".node_stack_" + startNode + "[" + i + "] has no payload");
                    continue;
                }
                Part payload = payloadAttach[i].attachedPart;
                if (payload.ShieldedFromAirstream)
                {
                    Log(LogType.Log, "skip, another fairing section already did " + payload.partInfo.name);
                    continue;
                }
                Log(LogType.Log, "shielding payload via part: " + payload.name);
                ShieldPart(payload);
            }
            shielding = true;

            // now add all surface attached parts that connect to a shielded part
            if (vessel != null && vessel.parts != null)
                for (int i = 0; i < 100; i++)
                    if (!AddRadialParts(i))
                        break;
        }

        void SetShielding(Part part, bool state)
        {
            //Log(LogType.Log, "SetShielding: part=" + part.partInfo.name + " shield=" + state);
            part.recheckShielding = !state;
            part.ShieldedFromAirstream = state;
        }

        private void ShieldPart(Part parent)
        {
            if (exempt.Contains(parent.partInfo.name))
            {
                Log(LogType.Log, "skip, exempt part: " + parent.partInfo.name);
                return;
            }
            if (part.physicalSignificance == Part.PhysicalSignificance.NONE)
            {
                Log(LogType.Log, "skip, no physics on " + parent.partInfo.name);
                return;
            }
            SetShielding(parent, true);
            shieldedParts.Add(parent);

            Log(LogType.Log, "attach nodes: " + parent.attachNodes.Count);
            foreach (AttachNode childAttach in parent.attachNodes)
            {
                Part child = childAttach.attachedPart;
                Log(LogType.Log, "search: " + parent.partInfo.name + "." + childAttach.id);
                if (child == null)
                {
                    Log(LogType.Log, "skip, nothing attached");
                    continue;
                }
                if (shieldedParts.Contains(child))
                {
                    Log(LogType.Log, "skip, already saw: " + child.partInfo.name);
                    continue;
                }
                Log(LogType.Log, "recursive shield: " + child.partInfo.name);
                ShieldPart(child);
            }
        }

        private bool AddRadialParts(int iteration)
        {
            Log(LogType.Log, "add radial iteration " + iteration);
            bool again = false;
            for (int i = vessel.parts.Count - 1; i >= 0; i--)
            {
                Part radial = vessel.parts[i];
                if (radial == null)
                {
                    Log(LogType.Warning, "no radial part");
                    continue; // should not happen
                }
                if (radial.srfAttachNode == null)
                    continue; // can this part surface attach to something?

                Part parent = radial.srfAttachNode.attachedPart;
                if (parent == null)
                    continue; // is this surface attached to something?
                if (shieldedParts.Contains(radial))
                {
                    Log(LogType.Log, "shield radial from: " + radial.partInfo.name + " into: " + parent.partInfo.name);
                    ShieldPart(parent);
                    again = true;
                    continue; // did we already add ourself to the list? just test parent
                }
                if (!shieldedParts.Contains(parent))
                    continue; // is the thing we're attached to shielded? then we are too

                Log(LogType.Log, "shield radial from: " + parent.partInfo.name + " into: " + radial.partInfo.name);
                ShieldPart(radial);
                again = true;
            }
            return again;
        }

        public void FixedUpdate()
        {
            if (shielding && isDecoupled)
                ExposePayload();
        }

        public void OnDestroy()
        {
            if (shielding)
                ExposePayload();
        }

        private void ExposePayload()
        {
            Log(LogType.Log, "payload exposed");
            shielding = false;
            foreach (Part p in shieldedParts)
                if (p != null)
                    SetShielding(p, false);
            shieldedParts = null;
        }
    }
}
