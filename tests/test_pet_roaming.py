"""Pet animation can reach distant destinations without changing game resources."""
import os
os.environ.setdefault('PYGAME_HIDE_SUPPORT_PROMPT', '1')
import random
import unittest

import courtyard
from pet_art import PetActor


class PetRoamingTests(unittest.TestCase):
    def test_distant_destination_is_reached(self):
        bounds = courtyard.PET_ACTIVITY
        actor = PetActor('p', '伙伴', '猫咪', x=100, y=600)
        actor._walk_to((1150, 650), bounds)
        rng = random.Random(7)
        for _ in range(120):
            actor.update(.25, bounds, rng)
            if actor.state != 'walk':
                break
        self.assertLess(abs(actor.x-1150), 3)
        self.assertLess(abs(actor.y-650), 3)
        self.assertNotEqual(actor.state, 'walk')

    def test_toy_visit_has_visible_play_on_arrival(self):
        bounds = courtyard.PET_ACTIVITY
        actor = PetActor('p', '伙伴', '猫咪', x=700, y=600, personality='活泼')
        rng = random.Random(42)
        actor._decide(bounds, rng, False, {'toy': courtyard.TOY, 'companions': []})
        self.assertEqual(actor.arrival_action, 'play')
        for _ in range(100):
            actor.update(.25, bounds, rng)
            if actor.state != 'walk':
                break
        self.assertEqual(actor.state, 'pet')
        self.assertIn('玩具', actor.behavior_note)


if __name__ == '__main__':
    unittest.main()
