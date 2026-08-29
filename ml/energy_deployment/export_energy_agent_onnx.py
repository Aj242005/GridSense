"""
GridSense - Section 5: PPO Energy Agent ONNX Export & Parity Verification Script.

Exports trained Stable-Baselines3 PPO Actor policy to ONNX format (opset 14)
for zero-overhead inference in Unity 6 via Sentis (com.unity.ai.inference).
Verifies action parity against ONNX Runtime across 200 random observation vectors.
"""

import os
import sys
import torch
import numpy as np

curr_dir = os.path.dirname(os.path.abspath(__file__))
if curr_dir not in sys.path:
    sys.path.insert(0, curr_dir)

from stable_baselines3 import PPO
import onnxruntime as ort


class OnnxPolicyWrapper(torch.nn.Module):
    """Wraps SB3 Actor policy into deterministic argmax action output for ONNX."""
    def __init__(self, policy):
        super().__init__()
        self.policy = policy

    def forward(self, obs: torch.Tensor):
        # obs shape: (batch, 8)
        latent_pi, _ = self.policy.mlp_extractor(obs)
        logits = self.policy.action_net(latent_pi)  # Shape: (batch, 8)
        action_idx = torch.argmax(logits, dim=-1, keepdim=True).to(torch.int64)  # Shape: (batch, 1)
        return action_idx


def export_and_verify(model_zip_path: str, output_onnx_path: str):
    if not os.path.exists(model_zip_path):
        raise FileNotFoundError(f"Model file not found: {model_zip_path}")

    print("=" * 75)
    print("GRID-SENSE SECTION 5: ONNX EXPORT & SENTIS PREPARATION")
    print(f"Loading trained PPO agent from: {model_zip_path}...")
    model = PPO.load(model_zip_path, device="cpu")

    wrapper = OnnxPolicyWrapper(model.policy)
    wrapper.eval()

    dummy_input = torch.zeros((1, 8), dtype=torch.float32)

    os.makedirs(os.path.dirname(output_onnx_path), exist_ok=True)
    print(f"Exporting to ONNX format (opset 14) at: {output_onnx_path}...")

    torch.onnx.export(
        wrapper,
        dummy_input,
        output_onnx_path,
        opset_version=14,
        input_names=["Observations"],
        output_names=["Actions"],
        dynamic_axes={
            "Observations": {0: "batch_size"},
            "Actions": {0: "batch_size"}
        },
        dynamo=False
    )

    print("[SUCCESS] ONNX model exported successfully.")

    # Parity verification against ONNX Runtime
    print("\nVerifying numerical action parity against ONNX Runtime across 200 test vectors...")
    session = ort.InferenceSession(output_onnx_path)

    np.random.seed(42)
    test_obs = np.random.uniform(0.0, 1.0, size=(200, 8)).astype(np.float32)

    with torch.no_grad():
        py_actions = wrapper(torch.from_numpy(test_obs)).numpy()

    onnx_inputs = {"Observations": test_obs}
    onnx_actions = session.run(None, onnx_inputs)[0]

    action_diff = np.abs(py_actions - onnx_actions)
    mismatches = np.sum(action_diff > 0)

    print(f"  Samples Tested: 200")
    print(f"  PyTorch vs ONNX Action Mismatches: {mismatches} / 200")

    if mismatches == 0:
        print("[SUCCESS] PERFECT 100% ACTION PARITY CONFIRMED (0 mismatches across all test states).")
    else:
        print(f"[FAIL] Parity verification detected {mismatches} mismatches.")

    # Save test vectors and expected actions for In-Engine Unity Sentis verification
    test_data = {
        "flat_observations": test_obs.flatten().tolist(),
        "expected_actions": py_actions.flatten().tolist()
    }
    json_path = os.path.join(curr_dir, "sentis_action_parity_vectors.json")
    with open(json_path, "w") as f:
        import json
        json.dump(test_data, f)
    print(f"Saved 200 parity vectors for Unity Sentis check to: {json_path}")

    print("=" * 75)
    return mismatches == 0


if __name__ == "__main__":
    default_model = os.path.join(curr_dir, "checkpoints", "ppo_energy_agent_production_final.zip")
    default_out = os.path.join(curr_dir, "..", "..", "Assets", "Data", "Models", "ppo_energy_deployment.onnx")
    export_and_verify(default_model, os.path.abspath(default_out))
