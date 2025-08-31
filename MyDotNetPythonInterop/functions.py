import numpy as np

def simulate_bid_ask(mid_price, base_spread=0.05, random_spread=True):
    if random_spread:
        spread = base_spread * (1 + 0.2 * np.random.randn(len(mid_price)))
        spread = np.clip(spread, 0.01, 0.2)
    else:
        spread = np.full_like(mid_price, base_spread)

    bid = mid_price - spread / 2
    ask = mid_price + spread / 2
    return bid, ask
