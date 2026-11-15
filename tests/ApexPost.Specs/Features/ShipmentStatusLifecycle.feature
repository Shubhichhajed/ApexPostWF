Feature: Shipment status lifecycle
  A shipment moves forward one step at a time:
  Created, PickedUp, InTransit, OutForDelivery, and finally Delivered or Failed.
  Skipping a step or going backwards would show customers a false tracking history.

  Background:
    Given the standard shipment lifecycle applies

  Scenario Outline: Only the next step in the lifecycle is allowed
    Given a shipment that is "<current>"
    When it is moved to "<next>"
    Then the move is <result>

    Examples:
      | current        | next           | result  |
      | Created        | PickedUp       | allowed |
      | PickedUp       | InTransit      | allowed |
      | InTransit      | OutForDelivery | allowed |
      | OutForDelivery | Delivered      | allowed |
      | OutForDelivery | Failed         | allowed |
      | Created        | Delivered      | refused |
      | InTransit      | PickedUp       | refused |
      | Failed         | OutForDelivery | refused |

  Scenario: A delivered shipment cannot go back on the road
    Given a shipment that is "Delivered"
    When it is moved to "InTransit"
    Then the move is refused with the message "Status transition from Delivered to InTransit is not allowed."
