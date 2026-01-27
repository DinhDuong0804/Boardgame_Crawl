"""
RabbitMQ Consumer for Translation Service
Listens for translation requests and processes them
"""
import json
import logging
import time
import signal
import sys
from typing import Callable

import pika
from pika.adapters.blocking_connection import BlockingChannel

import config
from translator import get_translator
from rulebook_processor import RulebookProcessor
from database import get_database

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)


class TranslationConsumer:
    """
    RabbitMQ consumer that listens for translation requests
    and processes them using OPUS-MT model
    """
    
    def __init__(self):
        self.connection = None
        self.channel = None
        self.translator = get_translator()
        self.rulebook_processor = RulebookProcessor()
        self.db = get_database()
        self.should_stop = False
        
    def connect(self):
        """Connect to RabbitMQ"""
        logger.info(f"Connecting to RabbitMQ at {config.RABBITMQ_HOST}:{config.RABBITMQ_PORT}")
        
        credentials = pika.PlainCredentials(
            config.RABBITMQ_USERNAME,
            config.RABBITMQ_PASSWORD
        )
        
        parameters = pika.ConnectionParameters(
            host=config.RABBITMQ_HOST,
            port=config.RABBITMQ_PORT,
            virtual_host=config.RABBITMQ_VHOST,
            credentials=credentials,
            heartbeat=600,
            blocked_connection_timeout=300
        )
        
        self.connection = pika.BlockingConnection(parameters)
        self.channel = self.connection.channel()
        
        # Declare exchange
        self.channel.exchange_declare(
            exchange=config.EXCHANGE_NAME,
            exchange_type='topic',
            durable=True
        )
        
        # Declare and bind queue
        self.channel.queue_declare(
            queue=config.QUEUE_TRANSLATION_REQUESTS,
            durable=True
        )
        
        self.channel.queue_bind(
            queue=config.QUEUE_TRANSLATION_REQUESTS,
            exchange=config.EXCHANGE_NAME,
            routing_key=config.ROUTING_KEY_REQUEST
        )
        
        # Set QoS - process one message at a time
        self.channel.basic_qos(prefetch_count=1)
        
        logger.info("RabbitMQ connected successfully")
    
    def process_message(self, ch: BlockingChannel, method, properties, body: bytes):
        """Process incoming translation request"""
        try:
            message = json.loads(body.decode('utf-8'))
            logger.info(f"Received translation request: game_id={message.get('game_id')}")
            
            # Update status to processing
            game_id = message.get('game_id')
            self.db.update_queue_status(game_id, 'processing')
            
            # Process translation
            result = self._translate_game(message)
            
            # Publish completion message
            self._publish_completion(result, properties.correlation_id)
            
            # Acknowledge message
            ch.basic_ack(delivery_tag=method.delivery_tag)
            
            logger.info(f"Translation completed for game_id={game_id}")
            
        except Exception as e:
            logger.error(f"Error processing message: {e}")
            # Reject and requeue on error
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)
    
    def _translate_game(self, message: dict) -> dict:
        """Translate game info and optionally rulebooks"""
        game_id = message.get('game_id')
        bgg_id = message.get('bgg_id')
        game_name = message.get('game_name', '')
        description = message.get('description', '')
        translate_info = message.get('translate_info', True)
        translate_rulebooks = message.get('translate_rulebooks', False)
        rulebooks = message.get('rulebooks', [])
        
        result = {
            'game_id': game_id,
            'success': True,
            'name_vi': None,
            'description_vi': None,
            'rulebooks': [],
            'error_message': None
        }
        
        try:
            # Load model
            self.translator.load()
            
            # Translate game info
            if translate_info:
                # Keep original English name (don't translate proper nouns)
                if game_name:
                    logger.info(f"Keeping original name: {game_name}")
                    result['name_vi'] = game_name  # Keep original English name
                
                if description:
                    logger.info(f"Translating description ({len(description)} chars)")
                    # Truncate very long descriptions
                    desc = description[:5000] if len(description) > 5000 else description
                    result['description_vi'] = self.translator.translate(desc)
                
                # Update database
                self.db.update_game_translation(
                    game_id=game_id,
                    name_vi=result['name_vi'],
                    description_vi=result['description_vi'],
                    success=True
                )
            
            # Translate rulebooks
            if translate_rulebooks:
                logger.info(f"Found {len(rulebooks)} rulebooks to process")
                if rulebooks:
                    for rb in rulebooks:
                        rb_result = self._translate_rulebook(rb, game_name, bgg_id)
                        result['rulebooks'].append(rb_result)
                else:
                    logger.warning("translate_rulebooks is True but rulebooks list is empty")
            
            # Update queue status
            self.db.update_queue_status(game_id, 'completed')
            
        except Exception as e:
            logger.error(f"Translation error for game {game_id}: {e}")
            result['success'] = False
            result['error_message'] = str(e)
            
            self.db.update_game_translation(
                game_id=game_id,
                name_vi=None,
                description_vi=None,
                success=False,
                error_message=str(e)
            )
            self.db.update_queue_status(game_id, 'failed', str(e))
        
        return result
    
    def _translate_rulebook(self, rulebook: dict, game_name: str, bgg_id: int) -> dict:
        """Translate a single rulebook"""
        rb_id = rulebook.get('rulebook_id')
        title = rulebook.get('title', 'Rules')
        url = rulebook.get('url', '')
        
        result = {
            'rulebook_id': rb_id,
            'success': False,
            'content_vi': None,
            'markdown_path': None,
            'error_message': None
        }
        
        try:
            # Use rulebook processor
            processed = self.rulebook_processor.process_rulebook(
                bgg_id=bgg_id,
                game_name=game_name,
                rulebook_url=url,
                rulebook_title=title,
                rulebook_id=rb_id
            )
            
            if processed:
                result['success'] = True
                result['markdown_path'] = processed.get('output_file')
                
                # Update database
                self.db.update_rulebook_translation(
                    rulebook_id=rb_id,
                    content_vi=None,  # Content is in markdown file
                    markdown_path=result['markdown_path'],
                    success=True
                )
            else:
                result['error_message'] = "Failed to process rulebook"
                self.db.update_rulebook_translation(
                    rulebook_id=rb_id,
                    content_vi=None,
                    markdown_path=None,
                    success=False,
                    error_message="Failed to process"
                )
                
        except Exception as e:
            result['error_message'] = str(e)
            self.db.update_rulebook_translation(
                rulebook_id=rb_id,
                content_vi=None,
                markdown_path=None,
                success=False,
                error_message=str(e)
            )
        
        return result
    
    def _publish_completion(self, result: dict, correlation_id: str):
        """Publish translation completion message"""
        if not self.channel:
            return
            
        message = json.dumps(result)
        
        self.channel.basic_publish(
            exchange=config.EXCHANGE_NAME,
            routing_key=config.ROUTING_KEY_COMPLETED,
            body=message.encode('utf-8'),
            properties=pika.BasicProperties(
                delivery_mode=2,  # Persistent
                content_type='application/json',
                correlation_id=correlation_id
            )
        )
        
        logger.debug(f"Published completion for game_id={result.get('game_id')}")
    
    def start(self):
        """Start consuming messages"""
        logger.info("Loading translation model...")
        self.translator.load()
        logger.info("Model loaded!")
        
        # Share Playwright instance if available (to avoid conflicts)
        if hasattr(self.translator, 'playwright') and self.translator.playwright:
            self.rulebook_processor.set_shared_playwright(self.translator.playwright)
        
        self.connect()
        
        # Setup graceful shutdown
        def signal_handler(sig, frame):
            logger.info("Shutdown signal received")
            self.should_stop = True
            if self.connection:
                self.connection.close()
            sys.exit(0)
        
        signal.signal(signal.SIGINT, signal_handler)
        signal.signal(signal.SIGTERM, signal_handler)
        
        logger.info(f"Waiting for messages on queue: {config.QUEUE_TRANSLATION_REQUESTS}")
        logger.info("Press Ctrl+C to exit")
        
        self.channel.basic_consume(
            queue=config.QUEUE_TRANSLATION_REQUESTS,
            on_message_callback=self.process_message,
            auto_ack=False
        )
        
        try:
            self.channel.start_consuming()
        except KeyboardInterrupt:
            logger.info("Consumer stopped")
        finally:
            if self.connection and not self.connection.is_closed:
                self.connection.close()
            self.db.close()


def main():
    """Main entry point for consumer"""
    consumer = TranslationConsumer()
    consumer.start()


if __name__ == "__main__":
    main()
