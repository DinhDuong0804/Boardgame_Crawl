import pika
import json
import config

def send_request():
    credentials = pika.PlainCredentials(config.RABBITMQ_USERNAME, config.RABBITMQ_PASSWORD)
    connection = pika.BlockingConnection(pika.ConnectionParameters(
        host=config.RABBITMQ_HOST,
        credentials=credentials
    ))
    channel = connection.channel()
    
    # Brass: Birmingham request
    message = {
        "game_id": 1,
        "bgg_id": 224517,
        "game_name": "Brass: Birmingham",
        "description": "An economic strategy game",
        "translate_info": True,
        "translate_rulebooks": True,
        "rulebooks": [
            {
                "rulebook_id": 5,
                "title": "Brass Birmingham reference sheet and context notes",
                "url": "https://boardgamegeek.com/filepage/example"
            }
        ]
    }
    
    channel.basic_publish(
        exchange=config.EXCHANGE_NAME,
        routing_key=config.ROUTING_KEY_REQUEST,
        body=json.dumps(message)
    )
    print(f"Sent request for: {message['game_name']}")
    connection.close()

if __name__ == "__main__":
    send_request()
