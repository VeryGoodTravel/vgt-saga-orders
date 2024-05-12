# vgt-saga-orders

Main repository of the saga process.
Contains both orders microservice and orchestrator

[Database, CQRS and Event Sourcing decribed bellow.](#db)

[SAGA transaction flow described bellow.](#saga)

## Repository

This repository contains additional submodules containing shared libraries of the SAGA microservices implementations.

To update those submodules in the local branch run:

    git submodule update --remote --merge

## Configuration

### Environmental variables

- RABBIT_HOST -> Address of the rabbit server.
- RABBIT_VIRT_HOST -> Virtual host of the rabbit server.
- RABBIT_PORT -> Port of the rabbit server.
- RABBIT_USR -> Username to log in with.
- RABBIT_PASSWORD -> User password to log in with.
- BACKEND_REQUESTS -> Queue name of the requests from the backend.
- BACKEND_REPLIES -> Exchange name to publish finished sagas to.
- RABBIT_REPLIES -> Queue of the replies sent back to the orchestrator.
- RABBIT_ORDER -> Queue of the requests sent by the orchestrator to the order service.
- RABBIT_PAYMENT -> Queue of the requests sent by the orchestrator to the payment gate service.
- RABBIT_HOTEL -> Queue of the requests sent by the orchestrator to the hotel service.
- RABBIT_FLIGHT -> Queue of the requests sent by the orchestrator to the flight service.
- DB_SERVER -> Database server name to use
- DB_NAME_SAGA -> Database name to use for the order service and the orchestrator
- DB_PASSWORD -> Database password to use for the database server
- DB_USER -> Username to use for the database server

## Implementation documentation
XML docs of the project available in the repository in the
file [SagaOrdersDocumentation.xml](SagaOrdersDocumentation.xml)

# <a name="db">Data storage (PostgreSQL)</a>

Follows CQRS and Event Sourcing.

Read Database of the CQRS consists of the one main table:
Transactions -> contains current information about each SAGA transaction with its SAGA state, 
answers from the services and the information on the trip parts.

![Database schema](DB_SAGA.png)

Event store is organised with the NEventStore C# library and has the following integration with CQRS:

![CQRS schema](CQRS.png)

# <a name="saga">SAGA transaction flow</a>

![SAGA flow chart](saga.svg)
