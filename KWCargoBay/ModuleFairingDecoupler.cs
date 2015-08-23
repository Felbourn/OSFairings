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

        private void Log(LogType mode, string message)
        {
            message = "ModuleFairingDecoupler - " + message;
            if (mode == LogType.Warning)
                Debug.LogWarning(message);
            else if (mode == LogType.Error)
                Debug.LogError(message);
            else if (!enableLogging)
                return;
            Debug.Log(message);
        }

        public override void OnStart(PartModule.StartState state)
        {
            base.OnStart(state);

            // I'm a nose fairing on a decoupler. Find my decoupler.
            AttachNode decouplerAttach = part.findAttachNode(explosiveNodeID);
            if (decouplerAttach == null)
            {
                Log(LogType.Error, "can't find decoupler node: node_stack_" + explosiveNodeID);
                return;
            }
            Log(LogType.Log, "using attachment from " + part.name + ".node_stack_" + explosiveNodeID);

            Part decoupler = decouplerAttach.attachedPart;
            if (decoupler == null)
            {
                Log(LogType.Warning, "not attached to decoupler, not shielding anything");
                return;
            }
            shieldedParts.Add(decoupler); // add decoupler so we don't recurse through it
            
            // I know the decoupler, so find its payload(s).
            Log(LogType.Log, "searching up from " + decoupler.name + ".node_stack_" + payloadNode);
            AttachNode[] payloadAttach = decoupler.findAttachNodes(payloadNode);
            if (payloadAttach == null)
            {
                Log(LogType.Error, "can't find payload node: node_stack_" + payloadNode);
                return;
            }

            foreach (var attachNode in payloadAttach) // handle all same-name nodes that can have payload
            {
                Part payload = attachNode.attachedPart;
                if (payload == null)
                    continue;
                if (payload.ShieldedFromAirstream)
                    continue; // another fairing section already did this

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
            parent.ShieldedFromAirstream = true;
            shieldedParts.Add(parent);

            foreach (AttachNode childAttach in parent.attachNodes)
            {
                Part child = childAttach.attachedPart;
                if (child == null)
                {
                    Log(LogType.Log, "no attach from: " + parent.partInfo.name + " at: " + childAttach.id);
                    continue;
                }
                if (shieldedParts.Contains(child))
                {
                    Log(LogType.Log, "seen: " + parent.partInfo.name + " from: " + child.partInfo.name);
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
