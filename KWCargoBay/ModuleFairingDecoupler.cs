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
        [KSPField(guiName = "Is Shielding", guiActive = true)]
        private bool shielding = false;

        [KSPField]
        public string payloadNode = "top";
        [KSPField]
        public bool enableLogging = true;

        private List<Part> shieldedParts = new List<Part>();
        private List<string> exempt = new List<String>();

        private void Log(LogType mode, string message)
        {
            message = "ModuleFairingDecoupler - " + message;
            if (mode == LogType.Warning)
                Debug.LogWarning(message);
            else if (mode == LogType.Error)
                Debug.LogError(message);
            else if (!enableLogging)
                return;
            else
                Debug.Log(message);
        }

        public override void OnLoad(ConfigNode node)
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
        }

        public override void OnStart(PartModule.StartState state)
        {
            base.OnStart(state);

            Log(LogType.Log, "fairing is '" + part.partInfo.name + "' attaching via explosiveNodeID 'node_stack_" + explosiveNodeID + "'");

            // I'm a nose fairing on a decoupler. Find my decoupler.
            AttachNode decouplerAttach = part.findAttachNode(explosiveNodeID);
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
            Log(LogType.Log, "fairing attached to '" + decoupler.partInfo.name + "' which should have payload on 'node_stack_" + payloadNode + "'");
            shieldedParts.Add(decoupler); // add decoupler so we don't recurse through it
            
            // I know the decoupler, so find its payload(s).
            AttachNode[] payloadAttach = decoupler.findAttachNodes(payloadNode);
            if (payloadAttach == null)
            {
                Log(LogType.Error, "can't find payload node: node_stack_" + payloadNode);
                return;
            }
            Log(LogType.Log, payloadAttach.Length + " of 'node_stack_" + payloadNode + "' found, searching all");

            for (int i = payloadAttach.Length - 1; i >= 0; i--) // handle all same-name nodes that can have payload
            {
                Log(LogType.Log, "search payload mount " + decoupler.name + ".node_stack_" + payloadNode + "[" + i + "]");
                if (payloadAttach[i] == null || payloadAttach[i].attachedPart == null)
                {
                    Log(LogType.Log, "skip, " + decoupler.name + ".node_stack_" + payloadNode + "[" + i + "] has no payload");
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
                    if (!AddRadialParts())
                        break;
        }

        private void ShieldPart(Part parent)
        {
            if (exempt.Contains(parent.partInfo.name))
            {
                Log(LogType.Log, "skip, exempt part: " + parent.partInfo.name);
                return;
            }
            parent.ShieldedFromAirstream = true;
            shieldedParts.Add(parent);

            foreach (AttachNode childAttach in parent.attachNodes)
            {
                Part child = childAttach.attachedPart;
                if (child == null)
                {
                    Log(LogType.Log, "skip, no attach from: " + parent.partInfo.name + " at: " + childAttach.id);
                    continue;
                }
                if (shieldedParts.Contains(child))
                {
                    Log(LogType.Log, "skip, seen: " + parent.partInfo.name + " from: " + child.partInfo.name);
                    continue;
                }
                Log(LogType.Log, "shield: " + child.partInfo.name + " via: " + parent.partInfo.name);
                ShieldPart(child);
            }
        }

        private bool AddRadialParts()
        {
            Log(LogType.Log, "add radial iteration");
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
                    continue; // is it surface attached to something?
                if (shieldedParts.Contains(radial))
                    continue; // did we already add ourself to the list?
                if (!shieldedParts.Contains(parent))
                    continue; // is the thing we're attached to shielded?

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
                    p.ShieldedFromAirstream = false;
            shieldedParts = null;
        }
    }
}
