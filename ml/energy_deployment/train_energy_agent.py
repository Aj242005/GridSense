"""
GridSense - Section 5: PPO Energy Deployment & Braking Policy Training Script.

Trains an offline PPO policy (via stable-baselines3) to manage:
- MGU-K Energy Deployment Mode (Push, Balanced, Hold, Save)
- Braking Aggressiveness (Normal vs Aggressive)

Environment: F1EnergyEnv (validated at <0.001% parity against Unity C# physics).
Outputs live training progress metrics to stdout and TensorBoard.
"""

import os
import sys
import time
import argparse
import numpy as np

# Ensure local ml directory is on import path
curr_dir = os.path.dirname(os.path.abspath(__file__))
if curr_dir not in sys.path:
    sys.path.insert(0, curr_dir)

import gymnasium as gym
from stable_baselines3 import PPO
from stable_baselines3.common.env_util import make_vec_env
from stable_baselines3.common.callbacks import CheckpointCallback, BaseCallback
from stable_baselines3.common.monitor import Monitor

from f1_energy_env import F1EnergyEnv


class LiveProgressBarCallback(BaseCallback):
    """Prints clear, human-readable episode completions and FPS milestones."""
    def __init__(self, check_freq: int = 1000):
        super().__init__()
        self.check_freq = check_freq
        self.last_time = time.time()
        self.last_step = 0

    def _on_step(self) -> bool:
        if self.n_calls % self.check_freq == 0:
            now = time.time()
            dt = now - self.last_time
            d_steps = self.num_timesteps - self.last_step
            fps = d_steps / max(dt, 0.001)
            self.last_time = now
            self.last_step = self.num_timesteps
            
            # Print single-line heartbeat
            print(f"[GridSense RL] Step: {self.num_timesteps:,} | Instantaneous FPS: {fps:.0f} | Episodes Completed: {len(self.model.ep_info_buffer)}")
        return True


def parse_args():
    parser = argparse.ArgumentParser(description="Train GridSense Section 5 Energy Deployment PPO Agent")
    parser.add_argument("--timesteps", type=int, default=20000, help="Total timesteps to train (default: 20,000 for benchmark)")
    parser.add_argument("--log-dir", type=str, default=os.path.join(curr_dir, "logs"), help="Directory for tensorboard logs")
    parser.add_argument("--save-dir", type=str, default=os.path.join(curr_dir, "checkpoints"), help="Directory for saving model checkpoints")
    parser.add_argument("--model-name", type=str, default="ppo_energy_agent", help="Base name of output model")
    parser.add_argument("--n-envs", type=int, default=4, help="Number of parallel vectorized environments")
    parser.add_argument("--learning-rate", type=float, default=3e-4, help="PPO learning rate")
    parser.add_argument("--batch-size", type=int, default=64, help="Mini-batch size")
    parser.add_argument("--n-steps", type=int, default=512, help="Steps per env per rollout update")
    return parser.parse_args()


def make_env():
    return Monitor(F1EnergyEnv())


def main():
    args = parse_args()
    os.makedirs(args.log_dir, exist_ok=True)
    os.makedirs(args.save_dir, exist_ok=True)

    print("=" * 80)
    print("GRID-SENSE SECTION 5: ENERGY DEPLOYMENT & BRAKING AGGRESSIVENESS AGENT")
    print(f"Target Timesteps: {args.timesteps:,}")
    print(f"Parallel Envs:    {args.n_envs}")
    print(f"Rollout Buffer:   {args.n_steps * args.n_envs:,} steps per policy update")
    print(f"Log Directory:    {args.log_dir}")
    print(f"Checkpoint Dir:   {args.save_dir}")
    print("=" * 80)

    # 1. Vectorized environments
    env = make_vec_env(make_env, n_envs=args.n_envs)

    # 2. PPO Policy Configuration
    policy_kwargs = dict(
        net_arch=dict(pi=[64, 64], vf=[64, 64])
    )

    model = PPO(
        policy="MlpPolicy",
        env=env,
        learning_rate=args.learning_rate,
        n_steps=args.n_steps,
        batch_size=args.batch_size,
        n_epochs=10,
        gamma=0.99,
        gae_lambda=0.95,
        clip_range=0.2,
        ent_coef=0.01,
        verbose=1,
        tensorboard_log=args.log_dir,
        policy_kwargs=policy_kwargs,
        seed=42
    )

    # 3. Callbacks
    checkpoint_callback = CheckpointCallback(
        save_freq=max(2500, args.timesteps // 5),
        save_path=args.save_dir,
        name_prefix=args.model_name
    )
    progress_callback = LiveProgressBarCallback(check_freq=1000)

    # 4. Train
    print("\n[START] Commencing PPO policy optimization...\n")
    start_time = time.time()

    model.learn(
        total_timesteps=args.timesteps,
        callback=[checkpoint_callback, progress_callback],
        progress_bar=False  # Handled cleanly via SB3 verbose=1 table + callback
    )

    elapsed_time = time.time() - start_time
    avg_fps = args.timesteps / max(elapsed_time, 0.001)

    print("\n" + "=" * 80)
    print(f"[COMPLETE] Training finished in {elapsed_time:.2f} seconds ({elapsed_time/60.0:.2f} minutes).")
    print(f"Average Throughput: {avg_fps:.1f} timesteps/sec")
    print("=" * 80)

    # 5. Save Final Model
    final_path = os.path.join(args.save_dir, f"{args.model_name}_final.zip")
    model.save(final_path)
    print(f"[SAVED] Final model saved to: {final_path}")

    # 6. Run Evaluation Lap
    print("\n[EVALUATION] Running evaluation lap with trained policy...")
    eval_env = F1EnergyEnv()
    obs, _ = eval_env.reset(seed=123)
    done = False
    ep_reward = 0.0
    action_history = []

    while not done:
        action, _ = model.predict(obs, deterministic=True)
        action_history.append(action)
        obs, reward, terminated, truncated, info = eval_env.step(action)
        ep_reward += reward
        done = terminated or truncated

    action_arr = np.array(action_history).flatten()
    deploy_arr = action_arr // 2
    braking_arr = action_arr % 2

    mode_names = {0: "Push", 1: "Balanced", 2: "Hold", 3: "Save"}
    deploy_dist = {name: 0.0 for name in mode_names.values()}
    for m in range(4):
        deploy_dist[mode_names[m]] = float(np.mean(deploy_arr == m) * 100.0)
    dist_str = ", ".join([f"{name}: {pct:.1f}%" for name, pct in deploy_dist.items()])

    agg_braking_pct = float(np.mean(braking_arr == 1) * 100.0)
    norm_braking_pct = float(np.mean(braking_arr == 0) * 100.0)

    print(f"  Lap Distance Completed: {info['distance_m']:.1f} / {eval_env.track_length_m:.1f} m")
    print(f"  Final Battery SoC:      {info['energy_pct']:.2f}% (Target band: 30-40%)")
    print(f"  Final Cumulative Wear:  {info['tyre_wear_pct']:.4f}%")
    print(f"  Peak Brake Rotor Temp:  {info.get('peak_brake_temp_c', info['brake_temp_c']):.1f}°C")
    print(f"  Final Brake Rotor Temp: {info['brake_temp_c']:.1f}°C")
    print(f"  Total Episode Reward:   {ep_reward:.2f}")
    print(f"  Deployment Mode Mix:    {dist_str}")
    print(f"  Braking Mode Mix:       Normal: {norm_braking_pct:.1f}%, Aggressive: {agg_braking_pct:.1f}%")
    print("=" * 80)


if __name__ == "__main__":
    main()
