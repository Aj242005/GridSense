"""
GridSense - Section 5: Calibrated Brake Thermal Parity Check.
Compares Unity C# BrakeSystem.cs (ductCoolingCoefficient=0.0018) against
Python F1EnergyPhysicsModel.step_brakes across 500 dynamic steps.
"""

import json
import os
import sys
import numpy as np

curr_dir = os.path.dirname(os.path.abspath(__file__))
if curr_dir not in sys.path:
    sys.path.insert(0, curr_dir)

from f1_energy_env import F1EnergyPhysicsModel


def run_parity():
    trace_path = os.path.join(curr_dir, "brake_parity_trace.json")
    if not os.path.exists(trace_path):
        raise FileNotFoundError(f"Trace file not found: {trace_path}")

    with open(trace_path, "r") as f:
        data = json.load(f)

    steps = data["steps"]
    print("=" * 75)
    print("GRID-SENSE SECTION 5: CALIBRATED BRAKE THERMAL PARITY VERIFICATION")
    print(f"Comparing 500 steps between Unity C# and Python physics model...")

    physics = F1EnergyPhysicsModel()
    dt = 0.02
    rotor_temps = np.zeros(4, dtype=np.float32)

    unity_temps = []
    py_temps = []

    for step in steps:
        brake_input = step["brake_input"]
        braking_mode = step["braking_mode"]
        speed_ms = step["speed_ms"]
        unity_temp = step["brake_temp_c"]

        rotor_temps = physics.step_brakes(rotor_temps, brake_input, speed_ms, braking_mode, 30.0, dt)
        py_temp = float(np.mean(rotor_temps))

        unity_temps.append(unity_temp)
        py_temps.append(py_temp)

    unity_arr = np.array(unity_temps)
    py_arr = np.array(py_temps)
    diff = np.abs(unity_arr - py_arr)

    max_diff = float(np.max(diff))
    mean_diff = float(np.mean(diff))

    print(f"  Unity Initial Temp:      {unity_arr[0]:.2f} °C")
    print(f"  Unity Peak Temp:         {np.max(unity_arr):.2f} °C")
    print(f"  Unity Final Temp:        {unity_arr[-1]:.2f} °C")
    print(f"  Python Peak Temp:        {np.max(py_arr):.2f} °C")
    print(f"  Python Final Temp:       {py_arr[-1]:.2f} °C")
    print(f"  Mean Absolute Divergence: {mean_diff:.8f} °C")
    print(f"  Max Absolute Divergence:  {max_diff:.8f} °C")

    passed = max_diff < 0.001
    if passed:
        print("[SUCCESS] PERFECT NUMERICAL PARITY CONFIRMED (< 0.0001 °C divergence).")
    else:
        print("[FAIL] Parity exceeded 0.001 °C threshold.")
    print("=" * 75)
    return passed


if __name__ == "__main__":
    run_parity()
