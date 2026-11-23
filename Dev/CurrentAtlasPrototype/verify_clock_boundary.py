"""Static source witness for clock publication and the two motion entry paths."""
import argparse
from pathlib import Path


def method(source, signature, following):
    begin = source.index(signature)
    return source[begin : source.index(following, begin)]


def verify_order(body, statements):
    cursor = -1
    for statement in statements:
        index = body.find(statement, cursor + 1)
        if index <= cursor or body.count(statement) != 1:
            raise ValueError(f"unverified boundary: {statement}")
        print(f"  +{body.count(chr(10), 0, index)} {statement}")
        cursor = index


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("game_source", type=Path)
    args = parser.parse_args()
    source = (args.game_source / "UBOAT.Game.Core.Time/GameTime.cs").read_text(encoding="utf-8-sig")
    skip = method(source, "public void SimulateTicks(long", "public void SimulateTicksLive(long")
    print("SimulateTicks")
    verify_order(skip, ("storyTicks += num;", "executionQueue.StoryTicks =", "sandbox.UpdateGroups(",
                        "Physics2D.Simulate(", "this.TicksSimulated?.Invoke(num);"))
    ordinary = method(source, "private void Update()", "private void LateUpdate()")
    print("Update")
    verify_order(ordinary, ("SimulateTicks(num2,", "storyTicksDelta = (long)", "storyTicks += storyTicksDelta;",
                            "executionQueue.StoryTicks =", "LaunchEvents();"))
    if "TicksSimulated" in ordinary:
        raise ValueError("unexpected ordinary-time TicksSimulated use")
    print("PASS static source ordering only; does not install or validate a runtime barrier")


if __name__ == "__main__":
    main()
