@api
Feature: Offline scan synchronisation
  Couriers record pickups and deliveries without signal. When the connection returns,
  the courier app uploads them. Every scan must reach the server exactly once,
  and a scan the server will never accept must be flagged instead of being retried forever.

  Background:
    Given courier "courier-demo-01" is signed in on the courier app
    And shipment "APX-70000001" is registered for delivery

  Scenario: Scans recorded offline are uploaded when the connection returns
    Given the courier recorded a "Pickup" scan at "Springfield Depot" while offline
    And the courier recorded an "InTransit" scan at "Springfield Route 4" while offline
    When the outbox syncs
    Then 2 scans are reported as accepted
    And every scan on the device is marked "Synced"
    And the server holds 2 scans for "APX-70000001"

  Scenario: A scan that was already uploaded is not stored twice
    Given the courier recorded a "Pickup" scan at "Springfield Depot" while offline
    And the outbox has synced
    And the same scan is queued again because the confirmation was lost
    When the outbox syncs
    Then the scan is reported as a duplicate
    And every scan on the device is marked "Synced"
    And the server holds 1 scan for "APX-70000001"

  Scenario Outline: Scans the server can never accept are flagged on the device
    Given the courier recorded <problem> for "<tracking number>" while offline
    When the outbox syncs
    Then every scan on the device is marked "Failed"
    And the server holds 0 scans for "<tracking number>"

    Examples:
      | problem                                   | tracking number |
      | a delivery without a recipient signature  | APX-70000001    |
      | a scan timed one hour in the future       | APX-70000001    |
      | a pickup                                  | APX-79999999    |
