@api
Feature: Shipment creation
  As a branch administrator
  I want to register parcels for delivery
  So that couriers can collect them and customers can track them

  Background:
    Given I am signed in as an administrator
    And the network offers a Standard rate for parcels up to 70 kg

  Scenario: A valid shipment is registered
    When I register shipment "APX-60000001" weighing 2.5 kg for "Alex Morgan" in "Springfield"
    Then the shipment is accepted
    And shipment "APX-60000001" can be looked up with status "Created"
    And its recipient city is "Springfield"

  Scenario: A tracking number cannot be used twice
    Given shipment "APX-60000002" has already been registered
    When I register shipment "APX-60000002" weighing 1.0 kg for "Sam Lee" in "Rivertown"
    Then the shipment is rejected as a duplicate

  Scenario Outline: Parcels outside the weight limits are refused
    When I register shipment "<tracking number>" weighing <weight> kg for "Alex Morgan" in "Springfield"
    Then the shipment is rejected because the weight is invalid
    And shipment "<tracking number>" does not exist

    Examples:
      | tracking number | weight | why                         |
      | APX-60000010    | 0      | nothing to send             |
      | APX-60000011    | 0.09   | just under the 0.1 kg limit |
      | APX-60000012    | 70.01  | just over the 70 kg limit   |

  Scenario: Couriers cannot register shipments
    Given I am signed in as courier "courier-demo-01"
    When I register shipment "APX-60000003" weighing 2.5 kg for "Alex Morgan" in "Springfield"
    Then the request is refused as forbidden
